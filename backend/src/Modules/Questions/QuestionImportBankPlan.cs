using System.Text.Json;
using Banks.Contracts;
using BuildingBlocks.Contracts;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

/// <summary>
/// 导入 JSON 里的 bank / pools 部分：解析成计划、在预览里展示、导入时落库。
/// 规则与题库设置页、选项池页保持一致，只是换了一种输入方式。
/// </summary>
internal sealed partial class QuestionImportService
{
    private const int MaxBankNameLength = 80;
    private const int MaxBankTagCount = 20;
    private const int MaxBankTagLength = 30;
    private const int MaxBankYearLength = 20;
    private const int MaxPoolNameLength = 60;
    private const int MaxPoolEntryLength = 200;
    private const int MaxPoolEntryTagCount = 10;

    private ImportBankPlan? BuildBankPlan(BankAccessDto access, JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value)
        {
            return null;
        }

        if (!access.IsOwner)
        {
            throw new ForbiddenException("只有题库创建者可以修改题库设置");
        }

        var bank = access.Bank;
        var name = ReadString(value, "name");
        if (name is not null)
        {
            name = name.Trim();
            if (name.Length is 0 or > MaxBankNameLength)
            {
                throw new ValidationFailedException("bank.name", $"题库名称不能为空且不超过 {MaxBankNameLength} 个字符");
            }
        }

        var description = ReadString(value, "description");
        var visibility = ReadString(value, "visibility")?.Trim();
        if (visibility is not null && visibility is not (BankVisibility.Public or BankVisibility.Private))
        {
            throw new ValidationFailedException("bank.visibility", "可见性只能是 private 或 public");
        }

        IReadOnlyList<string>? tags = null;
        if (value.TryGetProperty("tags", out _))
        {
            tags = NormalizeBankTags(ReadStringArray(value, "tags"));
        }

        var year = ReadString(value, "year")?.Trim();
        if (year is { Length: > MaxBankYearLength })
        {
            throw new ValidationFailedException("bank.year", $"学年不能超过 {MaxBankYearLength} 个字符");
        }

        var semester = ReadString(value, "semester")?.Trim();
        if (semester is not (null or "" or "上" or "下"))
        {
            throw new ValidationFailedException("bank.semester", "学期只能是上或下");
        }

        BankSettingsDto? settings = null;
        if (value.TryGetProperty("defaultShuffleOptions", out _) ||
            value.TryGetProperty("requireReview", out _))
        {
            settings = new BankSettingsDto(
                ReadBool(value, "defaultShuffleOptions") ?? bank.Settings.DefaultShuffleOptions,
                ReadBool(value, "requireReview") ?? bank.Settings.RequireReview);
        }

        var effectiveVisibility = visibility ?? bank.Visibility;
        var effectiveTags = tags ?? bank.Tags;
        if (effectiveVisibility == BankVisibility.Public && effectiveTags.Count == 0)
        {
            throw new ValidationFailedException("bank.tags", "公开题库至少需要一个标签");
        }

        return new ImportBankPlan(new UpdateBankRequest(
            name,
            description,
            visibility,
            settings,
            tags,
            year,
            semester));
    }

    private async Task<(
        List<ImportPoolPlan> Plans,
        List<OptionPoolDocument> Pools,
        Dictionary<string, List<string>> Entries,
        List<ImportIssueDto> Issues)> BuildPoolPlansAsync(
        string bankId,
        List<JsonElement>? elements,
        List<OptionPoolDocument> existingPools,
        CancellationToken cancellationToken)
    {
        var issues = new List<ImportIssueDto>();
        var plans = new List<ImportPoolPlan>();
        var pools = existingPools.ToList();
        var entries = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var pool in existingPools)
        {
            var docs = await poolStore.GetEntriesAsync(pool.Id, false, cancellationToken);
            entries[pool.Id] = docs.Select(entry => entry.Text).ToList();
        }

        if (elements is null || elements.Count == 0)
        {
            return (plans, pools, entries, issues);
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new ImportIssueDto(
                    ImportIssueCodes.Schema,
                    "pools 里的每项必须是对象",
                    ImportSeverity.Error));
                continue;
            }

            var name = ReadString(element, "name")?.Trim() ?? "";
            if (name.Length is 0 or > MaxPoolNameLength)
            {
                issues.Add(new ImportIssueDto(
                    ImportIssueCodes.Schema,
                    $"选项池名称不能为空且不超过 {MaxPoolNameLength} 个字符：{name}",
                    ImportSeverity.Error));
                continue;
            }

            if (!seenNames.Add(name))
            {
                issues.Add(new ImportIssueDto(
                    ImportIssueCodes.Schema,
                    $"pools 里重复出现同名选项池：{name}",
                    ImportSeverity.Error));
                continue;
            }

            var existing = pools.FirstOrDefault(pool => string.Equals(pool.Name, name, StringComparison.Ordinal));
            var delete = ReadBool(element, "delete") ?? false;

            if (delete)
            {
                if (existing is null)
                {
                    issues.Add(new ImportIssueDto(
                        ImportIssueCodes.PoolNotFound,
                        $"要删除的选项池不存在：{name}",
                        ImportSeverity.Warning));
                    continue;
                }

                var rules = await poolStore.GetReferencingRulesAsync(bankId, existing.Id, cancellationToken);
                if (rules.Count > 0)
                {
                    issues.Add(new ImportIssueDto(
                        ImportIssueCodes.PoolCapacity,
                        $"选项池「{name}」还有 {rules.Count} 道题在用，不能删除",
                        ImportSeverity.Error));
                    continue;
                }

                pools.Remove(existing);
                entries.Remove(existing.Id);
                plans.Add(new ImportPoolPlan(name, null, existing.Id, false, true, false, [], [], [], 0));
                continue;
            }

            var mode = (ReadString(element, "mode") ?? "merge").Trim().ToLowerInvariant();
            if (mode is not ("merge" or "replace"))
            {
                issues.Add(new ImportIssueDto(
                    ImportIssueCodes.Schema,
                    $"选项池「{name}」的 mode 只能是 merge 或 replace",
                    ImportSeverity.Error));
                continue;
            }

            var replace = mode == "replace";
            var description = ReadString(element, "description")?.Trim();
            var wanted = ReadPoolEntries(element, issues, name);
            if (wanted is null)
            {
                continue;
            }

            var now = DateTime.UtcNow;
            if (existing is null)
            {
                var poolId = Ids.New();
                plans.Add(new ImportPoolPlan(
                    name,
                    string.IsNullOrEmpty(description) ? null : description,
                    poolId,
                    true,
                    false,
                    replace,
                    wanted,
                    [],
                    [],
                    wanted.Count));
                pools.Add(new OptionPoolDocument
                {
                    Id = poolId,
                    BankId = bankId,
                    Name = name,
                    Description = description,
                    CreatedAt = now,
                });
                entries[poolId] = wanted.Select(entry => entry.Text).ToList();
                continue;
            }

            var docs = await poolStore.GetEntriesAsync(existing.Id, true, cancellationToken);
            var byText = docs
                .GroupBy(entry => entry.Text, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var wantedTexts = wanted.Select(entry => entry.Text).ToHashSet(StringComparer.Ordinal);

            var newEntries = wanted.Where(entry => !byText.ContainsKey(entry.Text)).ToList();
            var enable = wanted
                .Where(entry => byText.TryGetValue(entry.Text, out var doc) && doc.Disabled)
                .Select(entry => entry.Text)
                .ToList();
            var disable = replace
                ? docs.Where(doc => !doc.Disabled && !wantedTexts.Contains(doc.Text))
                    .Select(doc => doc.Text)
                    .ToList()
                : [];

            var enabledAfter = replace
                ? wantedTexts.Count
                : docs.Count(doc => !doc.Disabled) + newEntries.Count + enable.Count;

            var required = await poolStore.MaxRequiredAsync(bankId, existing.Id, cancellationToken);
            if (required > 0 && enabledAfter < required)
            {
                issues.Add(new ImportIssueDto(
                    ImportIssueCodes.PoolCapacity,
                    $"选项池「{name}」调整后只剩 {enabledAfter} 条可用，引用它的题目至少需要 {required} 条",
                    ImportSeverity.Error));
                continue;
            }

            plans.Add(new ImportPoolPlan(
                name,
                string.IsNullOrEmpty(description) ? null : description,
                existing.Id,
                false,
                false,
                replace,
                newEntries,
                enable,
                disable,
                enabledAfter));

            var effective = replace
                ? wantedTexts.ToList()
                : docs.Where(doc => !doc.Disabled).Select(doc => doc.Text)
                    .Concat(newEntries.Select(entry => entry.Text))
                    .Concat(enable)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            entries[existing.Id] = effective;
        }

        // 新建的池子也要参与题目校验（它们在导入时才真正落库）。
        return (plans, pools, entries, issues);
    }

    private List<ImportPoolEntryPlan>? ReadPoolEntries(
        JsonElement element,
        List<ImportIssueDto> issues,
        string poolName)
    {
        if (!element.TryGetProperty("entries", out var entriesElement) ||
            entriesElement.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new ImportIssueDto(
                ImportIssueCodes.Schema,
                $"选项池「{poolName}」缺少 entries 数组",
                ImportSeverity.Error));
            return null;
        }

        var result = new List<ImportPoolEntryPlan>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in entriesElement.EnumerateArray())
        {
            string? text;
            List<string> tags = [];
            if (item.ValueKind == JsonValueKind.String)
            {
                text = item.GetString()?.Trim();
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                text = ReadString(item, "text")?.Trim();
                tags = NormalizePoolTags(ReadStringArray(item, "tags"));
            }
            else
            {
                continue;
            }

            if (string.IsNullOrEmpty(text) || text.Length > MaxPoolEntryLength)
            {
                issues.Add(new ImportIssueDto(
                    ImportIssueCodes.Schema,
                    $"选项池「{poolName}」里有空的或超过 {MaxPoolEntryLength} 字的条目",
                    ImportSeverity.Error));
                return null;
            }

            if (seen.Add(text))
            {
                result.Add(new ImportPoolEntryPlan(text, tags));
            }
        }

        return result;
    }

    private async Task ApplyPlanAsync(
        CachedImportPlan plan,
        string bankId,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        if (plan.Bank is not null)
        {
            await banks.UpdateAsync(bankId, plan.Bank.Request, userId, cancellationToken);
        }

        if (plan.Pools is null || plan.Pools.Count == 0)
        {
            return;
        }

        var pools = provider.Collection<OptionPoolDocument>(QuestionPoolCollections.Pools);
        var entries = provider.Collection<OptionPoolEntryDocument>(QuestionPoolCollections.Entries);
        var now = DateTime.UtcNow;

        foreach (var pool in plan.Pools)
        {
            if (pool.Delete)
            {
                await pools.UpdateOneAsync(
                    x => x.Id == pool.PoolId,
                    Builders<OptionPoolDocument>.Update.Set(x => x.DeletedAt, now),
                    cancellationToken: cancellationToken);
                continue;
            }

            if (pool.IsNew)
            {
                await pools.InsertOneAsync(new OptionPoolDocument
                {
                    Id = pool.PoolId,
                    BankId = bankId,
                    Name = pool.Name,
                    Description = pool.Description,
                    CreatedAt = now,
                }, cancellationToken: cancellationToken);
            }
            else if (pool.Description is not null)
            {
                await pools.UpdateOneAsync(
                    x => x.Id == pool.PoolId,
                    Builders<OptionPoolDocument>.Update.Set(x => x.Description, pool.Description),
                    cancellationToken: cancellationToken);
            }

            foreach (var text in pool.EnableTexts)
            {
                await entries.UpdateOneAsync(
                    x => x.PoolId == pool.PoolId && x.Text == text,
                    Builders<OptionPoolEntryDocument>.Update.Set(x => x.Disabled, false),
                    cancellationToken: cancellationToken);
            }

            foreach (var text in pool.DisableTexts)
            {
                await entries.UpdateOneAsync(
                    x => x.PoolId == pool.PoolId && x.Text == text,
                    Builders<OptionPoolEntryDocument>.Update.Set(x => x.Disabled, true),
                    cancellationToken: cancellationToken);
            }

            foreach (var entry in pool.NewEntries)
            {
                var exists = await entries
                    .Find(x => x.PoolId == pool.PoolId && x.Text == entry.Text)
                    .AnyAsync(cancellationToken);
                if (exists)
                {
                    continue;
                }

                await entries.InsertOneAsync(new OptionPoolEntryDocument
                {
                    Id = Ids.New(),
                    PoolId = pool.PoolId,
                    BankId = bankId,
                    Text = entry.Text,
                    Tags = entry.Tags,
                    CreatedAt = now,
                }, cancellationToken: cancellationToken);
            }
        }
    }

    private static ImportSummaryDto BuildSummary(CachedImportPlan plan)
    {
        ImportBankChangeDto? bank = null;
        if (plan.Bank is { } bankPlan)
        {
            var request = bankPlan.Request;
            bank = new ImportBankChangeDto(
                request.Name,
                request.Description,
                request.Visibility,
                request.Tags,
                request.Year,
                request.Semester,
                request.Settings?.DefaultShuffleOptions,
                request.Settings?.RequireReview);
        }

        var pools = plan.Pools?
            .Select(pool => pool.ToDto())
            .ToList() ?? [];

        return new ImportSummaryDto(bank, pools);
    }

    private static List<string> NormalizeBankTags(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values)
        {
            var tag = raw?.Trim() ?? "";
            if (tag.Length == 0 || !seen.Add(tag))
            {
                continue;
            }

            if (tag.Length > MaxBankTagLength)
            {
                throw new ValidationFailedException("bank.tags", $"标签不能超过 {MaxBankTagLength} 个字符：{tag}");
            }

            result.Add(tag);
        }

        if (result.Count > MaxBankTagCount)
        {
            throw new ValidationFailedException("bank.tags", $"标签最多 {MaxBankTagCount} 个");
        }

        return result;
    }

    private static List<string> NormalizePoolTags(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values)
        {
            var tag = raw?.Trim() ?? "";
            if (tag.Length is 0 or > 30 || !seen.Add(tag))
            {
                continue;
            }

            result.Add(tag);
        }

        return result.Count > MaxPoolEntryTagCount ? result.Take(MaxPoolEntryTagCount).ToList() : result;
    }

    private static bool? ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
