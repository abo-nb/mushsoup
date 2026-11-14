using Banks.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

internal sealed class QuestionPoolService(QuestionPoolStore store, IBankDirectory banks, MongoDatabaseProvider provider)
{
    private IMongoCollection<OptionPoolDocument> Pools =>
        provider.Collection<OptionPoolDocument>(QuestionPoolCollections.Pools);

    private IMongoCollection<OptionPoolEntryDocument> Entries =>
        provider.Collection<OptionPoolEntryDocument>(QuestionPoolCollections.Entries);

    public async Task<IReadOnlyList<OptionPoolDto>> ListAsync(
        string bankId,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var access = await banks.GetAccessAsync(bankId, userId, classId, cancellationToken);
        EnsureCanView(access);

        var pools = await Pools
            .Find(x => x.BankId == bankId && x.DeletedAt == null)
            .Sort(Builders<OptionPoolDocument>.Sort.Ascending(x => x.CreatedAt))
            .ToListAsync(cancellationToken);

        var result = new List<OptionPoolDto>(pools.Count);
        foreach (var pool in pools)
        {
            var entries = await store.GetEntriesAsync(pool.Id, true, cancellationToken);
            result.Add(ToDto(pool, entries));
        }

        return result;
    }

    public async Task<PoolDetailDto> GetAsync(string poolId, string userId, string? classId, CancellationToken cancellationToken)
    {
        var (pool, _) = await RequirePoolAsync(poolId, userId, classId, cancellationToken);
        var entries = await store.GetEntriesAsync(pool.Id, true, cancellationToken);
        return new PoolDetailDto(
            ToDto(pool, entries),
            entries.Select(ToDto).ToList());
    }

    public async Task<OptionPoolDto> CreateAsync(
        string bankId,
        string userId,
        string? classId,
        CreatePoolRequest request,
        CancellationToken cancellationToken)
    {
        var access = await banks.GetAccessAsync(bankId, userId, classId, cancellationToken);
        EnsureCanView(access);
        if (!access!.CanEdit)
        {
            throw new ForbiddenException("没有权限在该题库中创建选项池");
        }

        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60)
        {
            throw new ValidationFailedException("name", "名称不能为空且不超过 60 个字符");
        }

        var now = DateTime.UtcNow;
        var pool = new OptionPoolDocument
        {
            Id = Ids.New(),
            BankId = bankId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAt = now,
        };

        await Pools.InsertOneAsync(pool, cancellationToken: cancellationToken);
        return ToDto(pool, []);
    }

    public async Task<OptionPoolDto> UpdateAsync(
        string poolId,
        string userId,
        string? classId,
        UpdatePoolRequest request,
        CancellationToken cancellationToken)
    {
        var (pool, access) = await RequirePoolAsync(poolId, userId, classId, cancellationToken);
        if (!access.CanEdit)
        {
            throw new ForbiddenException("没有权限修改这个选项池");
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length is 0 or > 60)
            {
                throw new ValidationFailedException("name", "名称不能为空且不超过 60 个字符");
            }

            pool.Name = name;
        }

        if (request.Description is not null)
        {
            pool.Description = request.Description.Trim().Length == 0 ? null : request.Description.Trim();
        }

        await Pools.ReplaceOneAsync(x => x.Id == pool.Id, pool, cancellationToken: cancellationToken);
        var entries = await store.GetEntriesAsync(pool.Id, true, cancellationToken);
        return ToDto(pool, entries);
    }

    public async Task DeleteAsync(string poolId, string userId, string? classId, CancellationToken cancellationToken)
    {
        var (pool, access) = await RequirePoolAsync(poolId, userId, classId, cancellationToken);
        if (!access.CanEdit)
        {
            throw new ForbiddenException("没有权限删除这个选项池");
        }

        var rules = await store.GetReferencingRulesAsync(pool.BankId, pool.Id, cancellationToken);
        if (rules.Count > 0)
        {
            throw new ValidationFailedException("poolId", $"还有 {rules.Count} 道题在使用这个选项池，先改掉它们再删除");
        }

        await Pools.UpdateOneAsync(
            x => x.Id == pool.Id,
            Builders<OptionPoolDocument>.Update.Set(x => x.DeletedAt, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    public async Task<PoolEntryDto> AddEntryAsync(
        string poolId,
        string userId,
        string? classId,
        CreatePoolEntryRequest request,
        CancellationToken cancellationToken)
    {
        var (pool, access) = await RequirePoolAsync(poolId, userId, classId, cancellationToken);
        if (!access.CanEdit)
        {
            throw new ForbiddenException("没有权限修改这个选项池");
        }

        var text = request.Text?.Trim() ?? "";
        if (text.Length is 0 or > 200)
        {
            throw new ValidationFailedException("text", "选项内容不能为空且不超过 200 个字符");
        }

        var exists = await Entries
            .Find(x => x.PoolId == pool.Id && x.Text == text)
            .AnyAsync(cancellationToken);
        if (exists)
        {
            throw new ConflictException("这个选项已经在池子里了");
        }

        var entry = new OptionPoolEntryDocument
        {
            Id = Ids.New(),
            PoolId = pool.Id,
            BankId = pool.BankId,
            Text = text,
            Tags = NormalizeTags(request.Tags),
            CreatedAt = DateTime.UtcNow,
        };

        await Entries.InsertOneAsync(entry, cancellationToken: cancellationToken);
        return ToDto(entry);
    }

    public async Task<PoolEntryDto> UpdateEntryAsync(
        string poolId,
        string entryId,
        string userId,
        string? classId,
        UpdatePoolEntryRequest request,
        CancellationToken cancellationToken)
    {
        var (pool, access) = await RequirePoolAsync(poolId, userId, classId, cancellationToken);
        if (!access.CanEdit)
        {
            throw new ForbiddenException("没有权限修改这个选项池");
        }

        var entry = await Entries
            .Find(x => x.Id == entryId && x.PoolId == pool.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("选项不存在");

        if (request.Disabled is true && !entry.Disabled)
        {
            await EnsureCapacityAsync(pool, 1, cancellationToken);
            entry.Disabled = true;
        }
        else if (request.Disabled is false)
        {
            entry.Disabled = false;
        }

        if (request.Text is not null)
        {
            var text = request.Text.Trim();
            if (text.Length is 0 or > 200)
            {
                throw new ValidationFailedException("text", "选项内容不能为空且不超过 200 个字符");
            }

            entry.Text = text;
        }

        if (request.Tags is not null)
        {
            entry.Tags = NormalizeTags(request.Tags);
        }

        await Entries.ReplaceOneAsync(x => x.Id == entry.Id, entry, cancellationToken: cancellationToken);
        return ToDto(entry);
    }

    public async Task DeleteEntryAsync(
        string poolId,
        string entryId,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var (pool, access) = await RequirePoolAsync(poolId, userId, classId, cancellationToken);
        if (!access.CanEdit)
        {
            throw new ForbiddenException("没有权限修改这个选项池");
        }

        var entry = await Entries
            .Find(x => x.Id == entryId && x.PoolId == pool.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("选项不存在");

        if (!entry.Disabled)
        {
            await EnsureCapacityAsync(pool, 1, cancellationToken);
        }

        await Entries.DeleteOneAsync(x => x.Id == entry.Id, cancellationToken);
    }

    public async Task<PoolHealthDto> HealthAsync(
        string poolId,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var (pool, _) = await RequirePoolAsync(poolId, userId, classId, cancellationToken);
        var entries = await store.GetEntriesAsync(pool.Id, true, cancellationToken);
        var available = entries.Count(entry => !entry.Disabled);
        var rules = await store.GetReferencingRulesAsync(pool.BankId, pool.Id, cancellationToken);
        var required = rules.Count == 0 ? 0 : rules.Max(rule => rule.Count) + 1;

        var warnings = new List<string>();
        if (required > 0 && available < required)
        {
            warnings.Add($"可用选项不足：被引用的题目至少需要 {required} 条，现在只有 {available} 条");
        }

        if (available < 6)
        {
            warnings.Add("选项少于 6 条，同一道题重复练习时很容易遇到相同干扰项");
        }

        var duplicates = entries
            .GroupBy(entry => QuestionNormalizer.Normalize(entry.Text), StringComparer.Ordinal)
            .Count(group => group.Count() > 1);
        if (duplicates > 0)
        {
            warnings.Add($"有 {duplicates} 组重复文本，抽题时会被自动去重");
        }

        return new PoolHealthDto(
            pool.Id,
            pool.Name,
            entries.Count,
            available,
            rules.Count,
            required,
            required == 0 || available >= required,
            warnings);
    }

    private async Task EnsureCapacityAsync(OptionPoolDocument pool, int removing, CancellationToken cancellationToken)
    {
        var required = await store.MaxRequiredAsync(pool.BankId, pool.Id, cancellationToken);
        if (required == 0)
        {
            return;
        }

        var available = (int)await store.CountAvailableAsync(pool.Id, cancellationToken) - removing;
        if (available < required)
        {
            throw new ValidationFailedException(
                "disabled",
                $"池子不够用了：被引用的题目至少需要 {required} 条可用选项，这次操作后只剩 {available} 条");
        }
    }

    private async Task<(OptionPoolDocument Pool, BankAccessDto Access)> RequirePoolAsync(
        string poolId,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var pool = await store.GetPoolAsync(poolId, cancellationToken)
            ?? throw new NotFoundException("选项池不存在");
        var access = await banks.GetAccessAsync(pool.BankId, userId, classId, cancellationToken);
        if (access is null || !access.CanView)
        {
            throw new NotFoundException("选项池不存在");
        }

        return (pool, access);
    }

    private static void EnsureCanView(BankAccessDto? access)
    {
        if (access is null || !access.CanView)
        {
            throw new NotFoundException("题库不存在");
        }
    }

    private static List<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        var result = new List<string>();
        foreach (var raw in tags ?? [])
        {
            var tag = raw?.Trim() ?? "";
            if (tag.Length > 0 && !result.Contains(tag, StringComparer.Ordinal))
            {
                result.Add(tag);
            }
        }

        if (result.Count > 10)
        {
            throw new ValidationFailedException("tags", "标签最多 10 个");
        }

        return result;
    }

    private static OptionPoolDto ToDto(OptionPoolDocument pool, IReadOnlyList<OptionPoolEntryDocument> entries) => new(
        pool.Id,
        pool.BankId,
        pool.Name,
        pool.Description,
        entries.Count,
        entries.Count(entry => !entry.Disabled),
        pool.CreatedAt);

    private static PoolEntryDto ToDto(OptionPoolEntryDocument entry) => new(
        entry.Id,
        entry.PoolId,
        entry.Text,
        entry.Tags.ToList(),
        entry.Disabled,
        entry.CreatedAt);
}
