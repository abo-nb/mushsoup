using Banks.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Classes.Contracts;
using Identity.Contracts;
using MongoDB.Driver;

namespace Banks;

file static class BankMapper
{
    public static BankDto ToDto(BankDocument bank, string? ownerName = null) => new(
        bank.Id,
        bank.ClassId,
        bank.OwnerId,
        bank.Name,
        bank.Description,
        bank.Visibility,
        bank.Tags,
        bank.Year,
        bank.Semester,
        new BankSettingsDto(bank.Settings.DefaultShuffleOptions, bank.Settings.RequireReview),
        bank.MemberIds,
        bank.QuestionCount,
        bank.CreatedAt,
        bank.UpdatedAt,
        ownerName);
}

internal sealed class BankService(
    MongoDatabaseProvider database,
    IClassDirectory classDirectory,
    IUserDirectory userDirectory,
    BankCache cache) : IBankDirectory
{
    private const int MaxNameLength = 80;
    private const int MaxTagCount = 20;
    private const int MaxTagLength = 30;
    private const int MaxYearLength = 20;

    private static readonly TimeSpan AccessTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MissingAccessTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ListTtl = TimeSpan.FromSeconds(30);

    private IMongoCollection<BankDocument> Banks => database.Collection<BankDocument>("banks");

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        await Banks.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<BankDocument>(
                Builders<BankDocument>.IndexKeys.Ascending(x => x.ClassId).Ascending(x => x.Visibility)),
            new CreateIndexModel<BankDocument>(
                Builders<BankDocument>.IndexKeys.Ascending(x => x.OwnerId)),
        ], cancellationToken);
    }

    public async Task<BankAccessDto?> GetAccessAsync(
        string bankId,
        string userId,
        string? classId,
        CancellationToken cancellationToken = default)
    {
        var key = cache.Key($"access:{bankId}:{userId}:{classId ?? ""}");
        if (cache.TryGet<BankAccessDto?>(key, out var cached))
        {
            return cached;
        }

        var bank = await FindAsync(bankId, cancellationToken);
        var access = bank is null ? null : ToAccess(bank, userId, classId);
        cache.Set<BankAccessDto?>(key, access, access is null ? MissingAccessTtl : AccessTtl);
        return access;
    }

    public async Task<IReadOnlyList<BankDto>> ListVisibleAsync(
        string userId,
        string? classId,
        CancellationToken cancellationToken = default)
    {
        var key = cache.Key($"list:{userId}:{classId ?? ""}");
        if (cache.TryGet<IReadOnlyList<BankDto>>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var f = Builders<BankDocument>.Filter;
        var visible = f.Or(
            f.Eq(x => x.OwnerId, userId),
            f.AnyEq(x => x.MemberIds, userId),
            f.Eq(x => x.Visibility, BankVisibility.Public));

        var banks = await Banks
            .Find(f.And(f.Eq(x => x.DeletedAt, (DateTime?)null), visible))
            .Sort(Builders<BankDocument>.Sort.Descending(x => x.CreatedAt))
            .ToListAsync(cancellationToken);

        var result = await WithOwnerNamesAsync(banks, cancellationToken);
        cache.Set<IReadOnlyList<BankDto>>(key, result, ListTtl);
        return result;
    }

    public async Task<IReadOnlyCollection<string>> ListPublicIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var key = cache.Key("public-ids");
        if (cache.TryGet<IReadOnlyCollection<string>>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var f = Builders<BankDocument>.Filter;
        var ids = await Banks
            .Find(f.And(
                f.Eq(x => x.DeletedAt, (DateTime?)null),
                f.Eq(x => x.Visibility, BankVisibility.Public)))
            .Project(x => x.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyCollection<string> result = ids;
        cache.Set(key, result, ListTtl);
        return result;
    }

    /// <summary>公开列表要显示创建者的 Discord 显示名，这里批量补齐，查不到就留空。</summary>
    private async Task<IReadOnlyList<BankDto>> WithOwnerNamesAsync(
        IReadOnlyList<BankDocument> banks,
        CancellationToken cancellationToken)
    {
        var ownerIds = banks
            .Select(bank => bank.OwnerId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ownerIds.Count == 0)
        {
            return banks.Select(bank => BankMapper.ToDto(bank)).ToList();
        }

        var users = await userDirectory.GetManyAsync(ownerIds, cancellationToken);
        var names = users.ToDictionary(user => user.Id, user => user.DisplayName, StringComparer.Ordinal);
        return banks
            .Select(bank => BankMapper.ToDto(bank, names.GetValueOrDefault(bank.OwnerId)))
            .ToList();
    }

    public async Task<bool> CanEditAsync(string bankId, string userId, CancellationToken cancellationToken = default)
    {
        var bank = await FindAsync(bankId, cancellationToken);
        return bank is not null && (bank.OwnerId == userId || bank.MemberIds.Contains(userId));
    }

    public async Task TouchQuestionCountAsync(string bankId, int delta, CancellationToken cancellationToken = default)
    {
        if (delta == 0)
        {
            return;
        }

        var f = Builders<BankDocument>.Filter;
        var idFilter = f.And(f.Eq(x => x.Id, bankId), f.Eq(x => x.DeletedAt, (DateTime?)null));
        await Banks.UpdateOneAsync(
            idFilter,
            Builders<BankDocument>.Update.Inc(x => x.QuestionCount, delta),
            cancellationToken: cancellationToken);

        if (delta < 0)
        {
            await Banks.UpdateOneAsync(
                f.And(f.Eq(x => x.Id, bankId), f.Lt(x => x.QuestionCount, 0)),
                Builders<BankDocument>.Update.Set(x => x.QuestionCount, 0),
                cancellationToken: cancellationToken);
        }

        cache.Invalidate();
    }

    public async Task<IReadOnlyDictionary<string, int>> CountOwnedByUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var f = Builders<BankDocument>.Filter;
        var owners = await Banks
            .Find(f.And(f.In(x => x.OwnerId, ids), f.Eq(x => x.DeletedAt, (DateTime?)null)))
            .Project(x => x.OwnerId)
            .ToListAsync(cancellationToken);

        return owners
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    public async Task<BankDto> CreateAsync(
        CreateBankRequest request,
        string userId,
        string? classId,
        CancellationToken cancellationToken = default)
    {
        var name = ValidateName(request.Name);
        var resolvedClassId = string.IsNullOrEmpty(classId)
            ? (await classDirectory.GetDefaultAsync(cancellationToken)).Id
            : classId;
        var now = DateTime.UtcNow;

        var bank = new BankDocument
        {
            Id = Ids.New(),
            ClassId = resolvedClassId,
            OwnerId = userId,
            Name = name,
            Description = NormalizeDescription(request.Description),
            Tags = NormalizeTags(request.Tags),
            Year = NormalizeYear(request.Year),
            Semester = NormalizeSemester(request.Semester),
            Visibility = BankVisibility.Private,
            Settings = new BankSettings
            {
                DefaultShuffleOptions = BankSettingsDto.Default.DefaultShuffleOptions,
                RequireReview = BankSettingsDto.Default.RequireReview,
            },
            MemberIds = [],
            QuestionCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await Banks.InsertOneAsync(bank, cancellationToken: cancellationToken);
        cache.Invalidate();
        return BankMapper.ToDto(bank);
    }

    public async Task<BankDto> UpdateAsync(
        string bankId,
        UpdateBankRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireOwnerAsync(bankId, userId, cancellationToken);
        var u = Builders<BankDocument>.Update;

        var updates = new List<UpdateDefinition<BankDocument>>();
        if (request.Name is not null)
        {
            updates.Add(u.Set(x => x.Name, ValidateName(request.Name)));
        }

        if (request.Description is not null)
        {
            updates.Add(u.Set(x => x.Description, NormalizeDescription(request.Description)));
        }

        var visibility = access.Bank.Visibility;
        if (request.Visibility is not null)
        {
            visibility = request.Visibility.Trim();
            if (visibility != BankVisibility.Private && visibility != BankVisibility.Public)
            {
                throw new ValidationFailedException("visibility", "可见性只能是 private 或 public");
            }

            updates.Add(u.Set(x => x.Visibility, visibility));
        }

        var tags = request.Tags is null ? access.Bank.Tags.ToList() : NormalizeTags(request.Tags);
        if (request.Tags is not null)
        {
            updates.Add(u.Set(x => x.Tags, tags));
        }

        if (request.Year is not null)
        {
            updates.Add(u.Set(x => x.Year, NormalizeYear(request.Year)));
        }

        if (request.Semester is not null)
        {
            updates.Add(u.Set(x => x.Semester, NormalizeSemester(request.Semester)));
        }

        EnsurePublicHasTags(visibility, tags);

        if (request.Settings is not null)
        {
            updates.Add(u.Set(x => x.Settings, new BankSettings
            {
                DefaultShuffleOptions = request.Settings.DefaultShuffleOptions,
                RequireReview = request.Settings.RequireReview,
            }));
        }

        updates.Add(u.Set(x => x.UpdatedAt, DateTime.UtcNow));

        var updated = await Banks.FindOneAndUpdateAsync(
            ActiveIdFilter(bankId),
            u.Combine(updates),
            new FindOneAndUpdateOptions<BankDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        if (updated is null)
        {
            throw new NotFoundException("题库不存在");
        }

        cache.Invalidate();
        return BankMapper.ToDto(updated);
    }

    public async Task DeleteAsync(string bankId, string userId, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(bankId, userId, cancellationToken);
        var now = DateTime.UtcNow;
        await Banks.UpdateOneAsync(
            ActiveIdFilter(bankId),
            Builders<BankDocument>.Update
                .Set(x => x.DeletedAt, now)
                .Set(x => x.UpdatedAt, now),
            cancellationToken: cancellationToken);
        cache.Invalidate();
    }

    public async Task<BankDto> AddMemberAsync(
        string bankId,
        AddBankMemberRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireOwnerAsync(bankId, userId, cancellationToken);
        var memberId = request.UserId?.Trim();

        if (string.IsNullOrEmpty(memberId) || !await userDirectory.ExistsAsync(memberId, cancellationToken))
        {
            throw new ValidationFailedException("userId", "用户不存在");
        }

        if (memberId == access.Bank.OwnerId || access.Bank.MemberIds.Contains(memberId))
        {
            return access.Bank;
        }

        var updated = await Banks.FindOneAndUpdateAsync(
            ActiveIdFilter(bankId),
            Builders<BankDocument>.Update
                .AddToSet(x => x.MemberIds, memberId)
                .Set(x => x.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<BankDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        if (updated is null)
        {
            throw new NotFoundException("题库不存在");
        }

        cache.Invalidate();
        return BankMapper.ToDto(updated);
    }

    public async Task<BankDto> RemoveMemberAsync(
        string bankId,
        string memberId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireOwnerAsync(bankId, userId, cancellationToken);

        if (memberId == access.Bank.OwnerId)
        {
            throw new ValidationFailedException("userId", "不能移除题库创建者");
        }

        var updated = await Banks.FindOneAndUpdateAsync(
            ActiveIdFilter(bankId),
            Builders<BankDocument>.Update
                .Pull(x => x.MemberIds, memberId)
                .Set(x => x.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<BankDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        if (updated is null)
        {
            throw new NotFoundException("题库不存在");
        }

        cache.Invalidate();
        return BankMapper.ToDto(updated);
    }

    private async Task<BankAccessDto> RequireOwnerAsync(string bankId, string userId, CancellationToken cancellationToken)
    {
        var access = await GetAccessAsync(bankId, userId, classId: null, cancellationToken)
            ?? throw new NotFoundException("题库不存在");

        if (!access.IsOwner)
        {
            throw new ForbiddenException("只有题库创建者可以执行此操作");
        }

        return access;
    }

    private static BankAccessDto ToAccess(BankDocument bank, string userId, string? classId)
    {
        var isOwner = bank.OwnerId == userId;
        var isMember = bank.MemberIds.Contains(userId);
        var canView = isOwner
            || isMember
            || bank.Visibility == BankVisibility.Public;

        return new BankAccessDto(BankMapper.ToDto(bank), isOwner, isMember, canView, isOwner || isMember);
    }

    private async Task<BankDocument?> FindAsync(string bankId, CancellationToken cancellationToken) =>
        await Banks.Find(ActiveIdFilter(bankId)).FirstOrDefaultAsync(cancellationToken);

    private static FilterDefinition<BankDocument> ActiveIdFilter(string bankId)
    {
        var f = Builders<BankDocument>.Filter;
        return f.And(f.Eq(x => x.Id, bankId), f.Eq(x => x.DeletedAt, (DateTime?)null));
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ValidationFailedException("name", "题库名称不能为空");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ValidationFailedException("name", $"题库名称不能超过 {MaxNameLength} 个字符");
        }

        return trimmed;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string NormalizeYear(string? year)
    {
        var trimmed = year?.Trim() ?? "";
        if (trimmed.Length > MaxYearLength)
        {
            throw new ValidationFailedException("year", $"学年不能超过 {MaxYearLength} 个字符");
        }

        return trimmed;
    }

    private static string NormalizeSemester(string? semester)
    {
        var trimmed = semester?.Trim() ?? "";
        if (trimmed.Length > 0 && trimmed != "上" && trimmed != "下")
        {
            throw new ValidationFailedException("semester", "学期只能是上或下");
        }

        return trimmed;
    }

    /// <summary>trim、去空、忽略大小写去重；数量与长度超限抛 422。</summary>
    private static List<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in tags ?? [])
        {
            var tag = raw?.Trim();
            if (string.IsNullOrEmpty(tag))
            {
                continue;
            }

            if (tag.Length > MaxTagLength)
            {
                throw new ValidationFailedException("tags", $"每个标签不能超过 {MaxTagLength} 字");
            }

            if (seen.Add(tag))
            {
                normalized.Add(tag);
            }
        }

        if (normalized.Count > MaxTagCount)
        {
            throw new ValidationFailedException("tags", $"标签最多 {MaxTagCount} 个");
        }

        return normalized;
    }

    private static void EnsurePublicHasTags(string visibility, IReadOnlyCollection<string> tags)
    {
        if (visibility == BankVisibility.Public && tags.Count == 0)
        {
            throw new ValidationFailedException("tags", "公开题库至少需要一个标签");
        }
    }
}
