using Banks.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Identity.Contracts;
using MongoDB.Driver;
using Ranges.Contracts;

namespace Ranges;

file static class RangeMapper
{
    public static PracticeRangeDto ToDto(
        RangeDocument range,
        string? ownerName,
        bool isMine) => new(
        range.Id,
        range.OwnerId,
        ownerName,
        range.BankId,
        range.BankName,
        range.Name,
        range.Description,
        range.Chapters.Select(chapter => (IReadOnlyList<string>)chapter).ToList(),
        range.QuestionCount,
        range.Limit,
        range.Mode,
        range.Algorithm,
        isMine,
        range.CreatedAt,
        range.UpdatedAt);
}

internal sealed class RangeService(
    MongoDatabaseProvider database,
    IBankDirectory banks,
    IUserDirectory users,
    RangeCache cache)
{
    private const int MaxNameLength = 60;
    private const int MaxDescriptionLength = 300;
    private const int MaxChapterCount = 50;
    private const int MaxPathSegmentLength = 60;
    private const int MaxQuestionCount = 100000;
    private const int MaxLimit = 100;
    private const int MaxListItems = 200;

    private static readonly TimeSpan ListTtl = TimeSpan.FromSeconds(30);

    private static readonly string[] KnownAlgorithms = ["sm2", "speed", "fsrs", "leitner"];

    private IMongoCollection<RangeDocument> Ranges =>
        database.Collection<RangeDocument>("practice_ranges");

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        await Ranges.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<RangeDocument>(
                Builders<RangeDocument>.IndexKeys
                    .Ascending(x => x.DeletedAt)
                    .Descending(x => x.CreatedAt),
                new CreateIndexOptions { Name = "deletedAt_createdAt" }),
            new CreateIndexModel<RangeDocument>(
                Builders<RangeDocument>.IndexKeys
                    .Ascending(x => x.BankId)
                    .Ascending(x => x.DeletedAt),
                new CreateIndexOptions { Name = "bankId_deletedAt" }),
        ], cancellationToken);
    }

    /// <summary>公开列表：只列所属题库当前仍公开的范围，创建者名批量补齐。</summary>
    public async Task<IReadOnlyList<PracticeRangeDto>> ListPublicAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        var publicBankIds = await banks.ListPublicIdsAsync(cancellationToken);
        if (publicBankIds.Count == 0)
        {
            return [];
        }

        var key = cache.Key($"list:{userId ?? ""}");
        if (cache.TryGet<IReadOnlyList<PracticeRangeDto>>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var f = Builders<RangeDocument>.Filter;
        var ranges = await Ranges
            .Find(f.And(
                f.Eq(x => x.DeletedAt, (DateTime?)null),
                f.In(x => x.BankId, publicBankIds)))
            .Sort(Builders<RangeDocument>.Sort.Descending(x => x.CreatedAt))
            .Limit(MaxListItems)
            .ToListAsync(cancellationToken);

        var ownerIds = ranges
            .Select(range => range.OwnerId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var names = ownerIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : (await users.GetManyAsync(ownerIds, cancellationToken))
                .ToDictionary(user => user.Id, user => user.DisplayName, StringComparer.Ordinal);

        var result = ranges
            .Select(range => RangeMapper.ToDto(
                range,
                names.GetValueOrDefault(range.OwnerId),
                userId is not null && range.OwnerId == userId))
            .ToList();

        cache.Set<IReadOnlyList<PracticeRangeDto>>(key, result, ListTtl);
        return result;
    }

    /// <summary>范围本身公开；所属题库转为私有后，只有它的创建者/成员还能通过链接打开。</summary>
    public async Task<PracticeRangeDto?> GetVisibleAsync(
        string rangeId,
        string? userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var range = await Ranges
            .Find(Builders<RangeDocument>.Filter.And(
                Builders<RangeDocument>.Filter.Eq(x => x.Id, rangeId),
                Builders<RangeDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null)))
            .FirstOrDefaultAsync(cancellationToken);

        if (range is null)
        {
            return null;
        }

        var access = await banks.GetAccessAsync(range.BankId, userId ?? "", classId, cancellationToken);
        if (access is null || !access.CanView)
        {
            return null;
        }

        var owner = await users.GetAsync(range.OwnerId, cancellationToken);
        return RangeMapper.ToDto(
            range,
            owner?.DisplayName,
            userId is not null && range.OwnerId == userId);
    }

    public async Task<PracticeRangeDto> CreateAsync(
        CreateRangeRequest request,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var bankId = request.BankId?.Trim() ?? "";
        var access = await banks.GetAccessAsync(bankId, userId, classId, cancellationToken)
            ?? throw new NotFoundException("题库不存在");

        if (!access.CanView)
        {
            throw new ForbiddenException("没有权限访问该题库");
        }

        if (access.Bank.Visibility != BankVisibility.Public)
        {
            throw new AppException("validation_failed", "题库还不是公开的，先到题库设置里公开", 422);
        }

        var name = ValidateName(request.Name);
        var description = NormalizeDescription(request.Description);
        var chapters = NormalizeChapters(request.Chapters);
        var mode = NormalizeMode(request.Mode);
        var algorithm = NormalizeAlgorithm(request.Algorithm);
        var now = DateTime.UtcNow;

        var range = new RangeDocument
        {
            Id = Ids.New(),
            OwnerId = userId,
            BankId = access.Bank.Id,
            BankName = access.Bank.Name,
            Name = name,
            Description = description,
            Chapters = chapters,
            QuestionCount = Math.Clamp(request.QuestionCount, 0, MaxQuestionCount),
            Limit = Math.Clamp(request.Limit, 1, MaxLimit),
            Mode = mode,
            Algorithm = algorithm,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await Ranges.InsertOneAsync(range, cancellationToken: cancellationToken);
        cache.Invalidate();

        var owner = await users.GetAsync(userId, cancellationToken);
        return RangeMapper.ToDto(range, owner?.DisplayName, isMine: true);
    }

    public async Task DeleteAsync(string rangeId, string userId, CancellationToken cancellationToken)
    {
        var f = Builders<RangeDocument>.Filter;
        var range = await Ranges
            .Find(f.And(f.Eq(x => x.Id, rangeId), f.Eq(x => x.DeletedAt, (DateTime?)null)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("范围不存在");

        if (range.OwnerId != userId)
        {
            throw new ForbiddenException("只有范围创建者可以删除");
        }

        var now = DateTime.UtcNow;
        await Ranges.UpdateOneAsync(
            f.Eq(x => x.Id, rangeId),
            Builders<RangeDocument>.Update
                .Set(x => x.DeletedAt, now)
                .Set(x => x.UpdatedAt, now),
            cancellationToken: cancellationToken);

        cache.Invalidate();
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ValidationFailedException("name", "范围名称不能为空");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ValidationFailedException("name", $"范围名称不能超过 {MaxNameLength} 个字符");
        }

        return trimmed;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new ValidationFailedException("description", $"简介不能超过 {MaxDescriptionLength} 个字符");
        }

        return trimmed;
    }

    /// <summary>每条章节路径是一串标题；去空白后至少一段、每段 1..60 字，最多 50 条路径。</summary>
    private static List<List<string>> NormalizeChapters(IReadOnlyList<IReadOnlyList<string>>? chapters)
    {
        if (chapters is null || chapters.Count == 0)
        {
            throw new ValidationFailedException("chapters", "至少选择一个章节");
        }

        if (chapters.Count > MaxChapterCount)
        {
            throw new ValidationFailedException("chapters", $"章节最多 {MaxChapterCount} 条");
        }

        var normalized = new List<List<string>>(chapters.Count);
        foreach (var chapter in chapters)
        {
            if (chapter is null || chapter.Count == 0)
            {
                throw new ValidationFailedException("chapters", "章节路径不能为空");
            }

            var segments = new List<string>(chapter.Count);
            foreach (var raw in chapter)
            {
                var segment = raw?.Trim();
                if (string.IsNullOrEmpty(segment))
                {
                    throw new ValidationFailedException("chapters", "章节路径不能包含空标题");
                }

                if (segment.Length > MaxPathSegmentLength)
                {
                    throw new ValidationFailedException("chapters", $"章节标题不能超过 {MaxPathSegmentLength} 个字符");
                }

                segments.Add(segment);
            }

            normalized.Add(segments);
        }

        return normalized;
    }

    private static string NormalizeMode(string? mode)
    {
        var trimmed = mode?.Trim().ToLowerInvariant() ?? "";
        if (trimmed.Length == 0)
        {
            return RangeMode.Daily;
        }

        if (!RangeMode.IsKnown(trimmed))
        {
            throw new ValidationFailedException("mode", "模式只能是 daily 或 endless");
        }

        return trimmed;
    }

    /// <summary>为空表示沿用练习者账号里的默认算法。</summary>
    private static string? NormalizeAlgorithm(string? algorithm)
    {
        var trimmed = algorithm?.Trim().ToLowerInvariant() ?? "";
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (!KnownAlgorithms.Contains(trimmed, StringComparer.Ordinal))
        {
            throw new ValidationFailedException("algorithm", "算法只能是 sm2、speed、fsrs 或 leitner");
        }

        return trimmed;
    }
}
