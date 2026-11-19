using System.Text.Json;
using System.Text.RegularExpressions;
using Banks.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

internal sealed class QuestionService(
    MongoDatabaseProvider provider,
    IBankDirectory banks,
    QuestionPoolStore pools,
    QuestionCache cache)
{
    private const int MaxChapterIndexBanks = 100;

    private static readonly TimeSpan ListTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ChapterIndexTtl = TimeSpan.FromSeconds(60);

    private IMongoCollection<QuestionDocument> Questions =>
        provider.Collection<QuestionDocument>(QuestionCollections.Questions);

    private IMongoCollection<QuestionRevisionDocument> Revisions =>
        provider.Collection<QuestionRevisionDocument>(QuestionCollections.Revisions);

    public async Task<IReadOnlyList<QuestionSummaryDto>> ListAsync(
        string bankId,
        string userId,
        string? classId,
        string? tag,
        string? query,
        CancellationToken cancellationToken)
    {
        var access = await banks.GetAccessAsync(bankId, userId, classId, cancellationToken);
        EnsureCanView(access);

        var normalizedTag = string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim();
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? "" : query.Trim();
        var key = cache.Key($"list:{bankId}:{normalizedTag}:{normalizedQuery}");
        if (cache.TryGet(key, out IReadOnlyList<QuestionSummaryDto>? cached) && cached is not null)
        {
            return cached;
        }

        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.BankId, bankId)
                     & Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null)
                     & Builders<QuestionDocument>.Filter.Ne(x => x.Status, QuestionStatus.Archived);

        if (normalizedTag.Length > 0)
        {
            filter &= Builders<QuestionDocument>.Filter.AnyEq(x => x.Tags, normalizedTag);
        }

        if (normalizedQuery.Length > 0)
        {
            filter &= Builders<QuestionDocument>.Filter.Regex(
                x => x.Stem,
                new BsonRegularExpression(Regex.Escape(normalizedQuery), "i"));
        }

        var documents = await Questions
            .Find(filter)
            .Sort(Builders<QuestionDocument>.Sort.Descending(x => x.UpdatedAt))
            .ToListAsync(cancellationToken);

        IReadOnlyList<QuestionSummaryDto> result = documents.Select(QuestionMapping.ToSummary).ToList();
        cache.Set(key, result, ListTtl);
        return result;
    }

    public async Task<IReadOnlyList<BankChapterIndexDto>> GetChapterIndexAsync(
        IReadOnlyList<string> bankIds,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var ids = bankIds
            .Select(bankId => bankId?.Trim() ?? "")
            .Where(bankId => bankId.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        if (ids.Count > MaxChapterIndexBanks)
        {
            throw new ValidationFailedException("bankIds", $"一次最多查询 {MaxChapterIndexBanks} 个题库");
        }

        var visibleIds = new List<string>();
        foreach (var bankId in ids)
        {
            var access = await banks.GetAccessAsync(bankId, userId, classId, cancellationToken);
            if (access is { CanView: true })
            {
                visibleIds.Add(bankId);
            }
        }

        if (visibleIds.Count == 0)
        {
            return [];
        }

        visibleIds.Sort(StringComparer.Ordinal);
        var key = cache.Key($"chapter-index:{string.Join(':', visibleIds)}");
        if (cache.TryGet(key, out IReadOnlyList<BankChapterIndexDto>? cached) && cached is not null)
        {
            return cached;
        }

        var filter = Builders<QuestionDocument>.Filter.In(x => x.BankId, visibleIds)
                     & Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null)
                     & Builders<QuestionDocument>.Filter.Ne(x => x.Status, QuestionStatus.Archived);

        var documents = await Questions
            .Find(filter)
            .Project(document => new { document.BankId, document.ChapterPath })
            .ToListAsync(cancellationToken);

        var chaptersByBank = visibleIds.ToDictionary(
            bankId => bankId,
            _ => new SortedSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var document in documents)
        {
            if (FirstChapter(document.ChapterPath) is { } chapter
                && chaptersByBank.TryGetValue(document.BankId, out var chapters))
            {
                chapters.Add(chapter);
            }
        }

        IReadOnlyList<BankChapterIndexDto> result = visibleIds
            .Select(bankId => new BankChapterIndexDto(bankId, chaptersByBank[bankId].ToList()))
            .ToList();
        cache.Set(key, result, ChapterIndexTtl);
        return result;
    }

    public async Task<QuestionDto> CreateAsync(
        string bankId,
        string userId,
        string? classId,
        SaveQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var access = await banks.GetAccessAsync(bankId, userId, classId, cancellationToken);
        EnsureCanView(access);
        if (!access!.CanEdit)
        {
            throw new ForbiddenException("没有权限在该题库中出题");
        }

        var prepared = await ValidateAsync(request, bankId, cancellationToken);
        var now = DateTime.UtcNow;
        var requireReview = access.Bank.Settings.RequireReview;

        var document = BuildDocument(bankId, userId, prepared, requireReview, now);

        await Questions.InsertOneAsync(document, cancellationToken: cancellationToken);
        await banks.TouchQuestionCountAsync(bankId, 1, cancellationToken);
        cache.Invalidate();

        return QuestionMapping.ToDto(document);
    }

    public async Task<IReadOnlyList<ImportCreateOutcome>> CreateManyForImportAsync(
        string bankId,
        string userId,
        string? classId,
        IReadOnlyList<SaveQuestionRequest> requests,
        string? importBatchId,
        CancellationToken cancellationToken)
    {
        var access = await banks.GetAccessAsync(bankId, userId, classId, cancellationToken);
        EnsureCanView(access);
        if (!access!.CanEdit)
        {
            throw new ForbiddenException("没有权限在该题库中出题");
        }

        var now = DateTime.UtcNow;
        var requireReview = access.Bank.Settings.RequireReview;
        var outcomes = new List<ImportCreateOutcome>(requests.Count);
        var documents = new List<QuestionDocument>(requests.Count);

        foreach (var request in requests)
        {
            try
            {
                // 导入计划在预览时已校验过选项池，避免大批量逐题查库。
                var prepared = await ValidateAsync(request, bankId, cancellationToken, skipPoolLookup: true);
                var document = BuildDocument(bankId, userId, prepared, requireReview, now, importBatchId);
                documents.Add(document);
                outcomes.Add(new ImportCreateOutcome(QuestionMapping.ToDto(document), null));
            }
            catch (AppException ex)
            {
                outcomes.Add(new ImportCreateOutcome(null, ex.Message));
            }
        }

        if (documents.Count > 0)
        {
            await Questions.InsertManyAsync(documents, cancellationToken: cancellationToken);
            await banks.TouchQuestionCountAsync(bankId, documents.Count, cancellationToken);
            cache.Invalidate();
        }

        return outcomes;
    }

    public async Task<QuestionDto> GetAsync(
        string questionId,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var document = await RequireActiveAsync(questionId, cancellationToken);
        var access = await banks.GetAccessAsync(document.BankId, userId, classId, cancellationToken);
        EnsureCanView(access);

        return QuestionMapping.ToDto(document);
    }

    public async Task<QuestionDto> UpdateAsync(
        string questionId,
        string userId,
        string? classId,
        SaveQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var document = await RequireActiveAsync(questionId, cancellationToken);
        var access = await banks.GetAccessAsync(document.BankId, userId, classId, cancellationToken);
        EnsureCanView(access);
        EnsureCanManage(document, access!, userId);

        var prepared = await ValidateAsync(request, document.BankId, cancellationToken);
        var now = DateTime.UtcNow;
        var requireReview = access!.Bank.Settings.RequireReview;

        document.Type = prepared.Type;
        document.Stem = prepared.Stem;
        document.Options = prepared.Options.Select(o => new QuestionOptionDocument
        {
            Key = o.Key,
            Text = o.Text,
            Correct = o.Correct,
        }).ToList();
        document.Shuffle = prepared.Shuffle;
        document.Tags = prepared.Tags;
        document.ChapterPath = prepared.ChapterPath;
        document.Difficulty = prepared.Difficulty;
        document.Source = prepared.Source;
        document.Explanation = prepared.Explanation;
        document.ContentHash = QuestionNormalizer.ComputeContentHash(prepared.Stem, prepared.Options.Select(o => o.Text));
        document.DistractorRule = prepared.Rule is null
            ? null
            : new DistractorRuleDocument { PoolId = prepared.Rule.PoolId, Count = prepared.Rule.Count };
        document.Version += 1;
        document.UpdatedAt = now;

        if (document.Status != QuestionStatus.Published)
        {
            document.Status = requireReview ? QuestionStatus.Pending : QuestionStatus.Published;
            document.Unreviewed = !requireReview;
        }

        await Questions.ReplaceOneAsync(x => x.Id == document.Id, document, cancellationToken: cancellationToken);

        await Revisions.InsertOneAsync(
            new QuestionRevisionDocument
            {
                Id = Ids.New(),
                QuestionId = document.Id,
                Version = document.Version,
                Type = document.Type,
                Stem = document.Stem,
                OptionsJson = JsonSerializer.Serialize(
                    prepared.Options.Select(o => new { key = o.Key, text = o.Text, correct = o.Correct })),
                AnswerKeysJson = JsonSerializer.Serialize(prepared.AnswerKeys),
                EditedBy = userId,
                EditedAt = now,
            },
            cancellationToken: cancellationToken);

        cache.Invalidate();
        return QuestionMapping.ToDto(document);
    }

    public async Task DeleteAsync(
        string questionId,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var document = await RequireActiveAsync(questionId, cancellationToken);
        var access = await banks.GetAccessAsync(document.BankId, userId, classId, cancellationToken);
        EnsureCanView(access);
        EnsureCanManage(document, access!, userId);

        var now = DateTime.UtcNow;
        var update = Builders<QuestionDocument>.Update
            .Set(x => x.DeletedAt, now)
            .Set(x => x.UpdatedAt, now);
        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.Id, document.Id)
                     & Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null);

        var result = await Questions.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.ModifiedCount == 0)
        {
            throw new NotFoundException("题目不存在");
        }

        await banks.TouchQuestionCountAsync(document.BankId, -1, cancellationToken);
        cache.Invalidate();
    }

    public async Task<IReadOnlyList<QuestionRevisionDto>> ListRevisionsAsync(
        string questionId,
        string userId,
        string? classId,
        CancellationToken cancellationToken)
    {
        var document = await RequireActiveAsync(questionId, cancellationToken);
        var access = await banks.GetAccessAsync(document.BankId, userId, classId, cancellationToken);
        EnsureCanView(access);

        var revisions = await Revisions
            .Find(Builders<QuestionRevisionDocument>.Filter.Eq(x => x.QuestionId, questionId))
            .Sort(Builders<QuestionRevisionDocument>.Sort.Descending(x => x.Version))
            .Limit(50)
            .ToListAsync(cancellationToken);

        return revisions.Select(QuestionMapping.ToRevisionDto).ToList();
    }

    private async Task<QuestionDocument> RequireActiveAsync(string questionId, CancellationToken cancellationToken)
    {
        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.Id, questionId)
                     & Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null);
        var document = await Questions.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document ?? throw new NotFoundException("题目不存在");
    }

    private static string? FirstChapter(IReadOnlyList<string> chapterPath)
    {
        foreach (var segment in chapterPath)
        {
            var chapter = segment?.Trim() ?? "";
            if (chapter.Length > 0)
            {
                return chapter;
            }
        }

        return null;
    }

    private static void EnsureCanView(BankAccessDto? access)
    {
        if (access is null || !access.CanView)
        {
            throw new NotFoundException("题库不存在");
        }
    }

    private static void EnsureCanManage(QuestionDocument document, BankAccessDto access, string userId)
    {
        if (document.CreatedBy != userId && !access.IsOwner)
        {
            throw new ForbiddenException("只有题目创建者或题库所有者可以修改该题目");
        }
    }

    private static QuestionDocument BuildDocument(
        string bankId,
        string userId,
        PreparedQuestion prepared,
        bool requireReview,
        DateTime now,
        string? importBatchId = null)
    {
        return new QuestionDocument
        {
            Id = Ids.New(),
            BankId = bankId,
            Type = prepared.Type,
            Stem = prepared.Stem,
            Options = prepared.Options.Select(o => new QuestionOptionDocument
            {
                Key = o.Key,
                Text = o.Text,
                Correct = o.Correct,
            }).ToList(),
            Shuffle = prepared.Shuffle,
            Tags = prepared.Tags,
            ChapterPath = prepared.ChapterPath,
            Difficulty = prepared.Difficulty,
            Source = prepared.Source,
            Explanation = prepared.Explanation,
            Status = requireReview ? QuestionStatus.Pending : QuestionStatus.Published,
            Unreviewed = !requireReview,
            Version = 1,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
            ContentHash = QuestionNormalizer.ComputeContentHash(prepared.Stem, prepared.Options.Select(o => o.Text)),
            DistractorRule = prepared.Rule is null
                ? null
                : new DistractorRuleDocument { PoolId = prepared.Rule.PoolId, Count = prepared.Rule.Count },
            ImportBatchId = importBatchId,
        };
    }

    private async Task<PreparedQuestion> ValidateAsync(
        SaveQuestionRequest request,
        string bankId,
        CancellationToken cancellationToken,
        bool skipPoolLookup = false)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Fail(string field, string message)
        {
            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors[field] = messages;
            }

            messages.Add(message);
        }

        var type = request.Type?.Trim() ?? "";
        if (!QuestionTypes.IsKnown(type))
        {
            Fail("type", "题型必须是 single_choice 或 multiple_choice");
        }

        var stem = request.Stem?.Trim() ?? "";
        if (stem.Length == 0)
        {
            Fail("stem", "题干不能为空");
        }
        else if (stem.Length > 4000)
        {
            Fail("stem", "题干不能超过 4000 个字符");
        }

        var rule = request.DistractorRule;
        var options = new List<QuestionOptionDto>();
        var optionKeys = new HashSet<string>(StringComparer.Ordinal);
        var optionInputs = request.Options ?? [];

        // 有选项池时，题目只需要写出正确答案，干扰项由池子提供。
        var minOptions = rule is null ? 2 : 1;
        if (optionInputs.Count < minOptions || optionInputs.Count > 10)
        {
            Fail("options", rule is null
                ? "选项数量必须在 2 到 10 之间"
                : "选项池模式下，题目自身的选项数量必须在 1 到 10 之间");
        }

        foreach (var option in optionInputs)
        {
            var key = option.Key?.Trim() ?? "";
            if (key.Length == 0)
            {
                Fail("options", "选项 key 不能为空");
                continue;
            }

            if (!optionKeys.Add(key))
            {
                Fail("options", $"选项 key 不能重复：{key}");
                continue;
            }

            options.Add(new QuestionOptionDto(key, option.Text ?? "", false));
        }

        if (rule is not null)
        {
            if (rule.Count is < 1 or > 9)
            {
                Fail("distractorRule", "干扰项数量必须在 1 到 9 之间");
            }
            else if (!skipPoolLookup)
            {
                var pool = await pools.GetPoolAsync(rule.PoolId, cancellationToken);
                if (pool is null || pool.BankId != bankId)
                {
                    Fail("distractorRule", "选项池不存在");
                }
                else
                {
                    var entries = await pools.GetEntriesAsync(pool.Id, false, cancellationToken);
                    var used = options
                        .Select(option => QuestionNormalizer.Normalize(option.Text))
                        .ToHashSet(StringComparer.Ordinal);
                    var usable = entries.Count(entry =>
                        used.Add(QuestionNormalizer.Normalize(entry.Text)));

                    if (usable < rule.Count + 1)
                    {
                        Fail(
                            "distractorRule",
                            $"选项池至少需要 {rule.Count + 1} 条可用选项，现在只有 {usable} 条");
                    }

                    if (options.Count + rule.Count > 10)
                    {
                        Fail("distractorRule", "题目选项加上池子抽取的干扰项不能超过 10 个");
                    }
                }
            }
        }

        var totalOptions = options.Count + (rule?.Count ?? 0);

        var answerKeys = (request.Answer ?? [])
            .Select(key => key?.Trim() ?? "")
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (answerKeys.Count == 0)
        {
            Fail("answer", "答案不能为空");
        }

        foreach (var key in answerKeys.Where(key => !optionKeys.Contains(key)))
        {
            Fail("answer", $"答案 key 不在选项中：{key}");
        }

        if (type == QuestionTypes.SingleChoice && answerKeys.Count != 1)
        {
            Fail("answer", "单选题必须恰好 1 个正确答案");
        }

        if (type == QuestionTypes.MultipleChoice)
        {
            if (answerKeys.Count < 2)
            {
                Fail("answer", "多选题至少需要 2 个正确答案");
            }
            else if (answerKeys.Count >= totalOptions)
            {
                Fail("answer", "多选题的正确答案数量必须少于选项总数");
            }
        }

        var shuffle = string.IsNullOrWhiteSpace(request.Shuffle) ? ShuffleMode.Follow : request.Shuffle.Trim();
        if (shuffle is not (ShuffleMode.Follow or ShuffleMode.Always or ShuffleMode.Never))
        {
            Fail("shuffle", "shuffle 只能是 follow、always 或 never");
        }

        var difficulty = request.Difficulty ?? 3;
        if (difficulty is < 1 or > 5)
        {
            Fail("difficulty", "难度必须在 1 到 5 之间");
        }

        var tags = new List<string>();
        foreach (var raw in request.Tags ?? [])
        {
            var tag = raw?.Trim() ?? "";
            if (tag.Length > 0 && !tags.Contains(tag, StringComparer.Ordinal))
            {
                tags.Add(tag);
            }
        }

        if (tags.Count > 10)
        {
            Fail("tags", "标签最多 10 个");
        }

        var chapterPath = ChapterPaths.Normalize(request.ChapterPath);
        if (ChapterPaths.FindProblem(chapterPath) is { } chapterProblem)
        {
            Fail("chapterPath", chapterProblem);
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(
                errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
        }

        var answerSet = answerKeys.ToHashSet(StringComparer.Ordinal);
        var finalOptions = options
            .Select(option => option with { Correct = answerSet.Contains(option.Key) })
            .ToList();

        return new PreparedQuestion(
            type,
            stem,
            finalOptions,
            answerKeys,
            shuffle,
            tags,
            difficulty,
            string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim(),
            request.Explanation?.Trim() ?? "",
            rule is null ? null : new DistractorRuleDto(rule.PoolId, rule.Count),
            chapterPath);
    }

    private sealed record PreparedQuestion(
        string Type,
        string Stem,
        List<QuestionOptionDto> Options,
        List<string> AnswerKeys,
        string Shuffle,
        List<string> Tags,
        int Difficulty,
        string? Source,
        string Explanation,
        DistractorRuleDto? Rule,
        List<string> ChapterPath);
}

internal sealed record ImportCreateOutcome(QuestionDto? Question, string? Error);
