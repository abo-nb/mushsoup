using System.Text;
using System.Text.Json;
using Banks.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

internal sealed class QuestionImportService(
    QuestionService questions,
    IBankDirectory banks,
    ICurrentUser currentUser,
    MongoDatabaseProvider provider,
    ImportPlanCache plans,
    ImportJobStore jobs,
    IServiceScopeFactory scopeFactory,
    QuestionPoolStore poolStore)
{
    private const int MaxQuestionsPerImport = 10000;
    private const int MaxPayloadBytes = 50 * 1024 * 1024;
    private const int ImportChunkSize = 200;

    private IMongoCollection<ImportBatchDocument> Batches =>
        provider.Collection<ImportBatchDocument>(ImportCollections.Batches);

    private IMongoCollection<ImportBatchItemRecord> BatchItems =>
        provider.Collection<ImportBatchItemRecord>(ImportCollections.BatchItems);

    private IMongoCollection<QuestionDocument> Questions =>
        provider.Collection<QuestionDocument>(QuestionCollections.Questions);

    public async Task<ImportPreviewDto> PreviewAsync(string bankId, string payload, CancellationToken cancellationToken)
    {
        var plan = await BuildPlanAsync(bankId, payload, cancellationToken);
        var planId = plans.Store(currentUser.RequireUserId(), bankId, plan);
        var samples = plan.Candidates
            .Where(candidate => !candidate.HasError && candidate.DistractorRule is null)
            .Take(3)
            .Select(candidate => CardAssembler.Assemble(
                $"preview-{candidate.Index}",
                candidate.Type ?? QuestionTypes.SingleChoice,
                candidate.Stem,
                candidate.Options.Select(option => (option.Key, option.Text)).ToList(),
                "import-preview",
                0,
                shuffle: true))
            .ToList();

        return new ImportPreviewDto(
            plan.Candidates.Count,
            plan.Candidates.Count(candidate => candidate.Status == "valid"),
            plan.Candidates.Count(candidate => candidate.Status == "warning"),
            plan.Candidates.Count(candidate => candidate.HasError),
            plan.Candidates.Select(candidate => candidate.ToPreview()).ToList(),
            plan.BatchIssues,
            samples,
            planId);
    }

    public async Task<ImportResultDto> ImportAsync(string bankId, ImportRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var plan = await BuildPlanAsync(bankId, request.Payload ?? "", cancellationToken);
        var run = await RunImportAsync(null, plan, bankId, userId, currentUser.ClassId, request.Force, cancellationToken);

        return new ImportResultDto(
            run.BatchId,
            run.Total,
            run.Imported,
            run.Skipped,
            run.Failed,
            run.BatchIssues,
            run.Items.Select(ToPreview).ToList());
    }

    public async Task<ImportJobStartedDto> StartJobAsync(
        string bankId,
        ImportJobStartRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var classId = currentUser.ClassId;

        CachedImportPlan plan;
        if (!string.IsNullOrWhiteSpace(request.PlanId))
        {
            var access = await RequireAccessAsync(bankId, userId, cancellationToken);
            if (!access.CanEdit)
            {
                throw new ForbiddenException("没有权限向该题库导入题目");
            }

            plan = plans.Take(request.PlanId, userId, bankId)
                ?? throw new ValidationFailedException("planId", "导入计划已失效，请重新预览后再导入");
        }
        else
        {
            plan = await BuildPlanAsync(bankId, request.Payload ?? "", cancellationToken);
        }

        var job = jobs.Create(userId, bankId);
        job.Begin(plan.Candidates.Count);

        _ = Task.Run(
            () => ExecuteJobAsync(job, plan, bankId, userId, classId, request.Force),
            CancellationToken.None);

        return new ImportJobStartedDto(job.Id);
    }

    public ImportJobDto? GetJob(string jobId)
    {
        var userId = currentUser.RequireUserId();
        var job = jobs.Get(jobId);
        return job is null || job.UserId != userId ? null : job.ToDto();
    }

    private async Task ExecuteJobAsync(
        ImportJob job,
        CachedImportPlan plan,
        string bankId,
        string userId,
        string? classId,
        bool force)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<QuestionImportService>();
            var result = await runner.RunImportAsync(job, plan, bankId, userId, classId, force, CancellationToken.None);
            job.Complete(result.BatchId);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
        }
    }

    private async Task<ImportRunResult> RunImportAsync(
        ImportJob? job,
        CachedImportPlan plan,
        string bankId,
        string userId,
        string? classId,
        bool force,
        CancellationToken cancellationToken)
    {
        var batchId = Ids.New();
        var items = new List<ImportBatchItemDocument>(plan.Candidates.Count);
        var processed = 0;
        var imported = 0;
        var skipped = 0;
        var failed = 0;
        var pending = 0;

        for (var offset = 0; offset < plan.Candidates.Count; offset += ImportChunkSize)
        {
            var chunk = plan.Candidates.Skip(offset).Take(ImportChunkSize).ToList();
            var chunkItems = new ImportBatchItemDocument?[chunk.Count];
            var requests = new List<SaveQuestionRequest>(chunk.Count);
            var positions = new List<int>(chunk.Count);

            for (var index = 0; index < chunk.Count; index++)
            {
                var candidate = chunk[index];
                if (candidate.HasError)
                {
                    failed += 1;
                    chunkItems[index] = ToDocument(candidate, "error", null);
                    continue;
                }

                if (candidate.Duplicate && !force)
                {
                    skipped += 1;
                    chunkItems[index] = ToDocument(candidate, "skipped", null);
                    continue;
                }

                requests.Add(candidate.ToRequest());
                positions.Add(index);
            }

            if (requests.Count > 0)
            {
                var outcomes = await questions.CreateManyForImportAsync(
                    bankId,
                    userId,
                    classId,
                    requests,
                    batchId,
                    cancellationToken);

                for (var index = 0; index < positions.Count; index++)
                {
                    var candidate = chunk[positions[index]];
                    var outcome = index < outcomes.Count ? outcomes[index] : null;

                    if (outcome?.Question is { } created)
                    {
                        imported += 1;
                        if (created.Status == QuestionStatus.Pending)
                        {
                            pending += 1;
                        }

                        chunkItems[positions[index]] = ToDocument(candidate, "imported", created.Id);
                    }
                    else
                    {
                        failed += 1;
                        candidate.Issues.Add(new ImportIssueDto(
                            ImportIssueCodes.Schema,
                            outcome?.Error ?? "导入失败",
                            ImportSeverity.Error,
                            candidate.Index));
                        chunkItems[positions[index]] = ToDocument(candidate, "failed", null);
                    }
                }
            }

            var records = new List<ImportBatchItemRecord>(chunk.Count);
            for (var index = 0; index < chunk.Count; index++)
            {
                if (chunkItems[index] is not { } document)
                {
                    continue;
                }

                records.Add(ImportBatchItemRecord.From(batchId, chunk[index].Index, document));
                if (job is null)
                {
                    items.Add(document);
                }
            }

            if (records.Count > 0)
            {
                await BatchItems.InsertManyAsync(records, cancellationToken: cancellationToken);
            }

            processed += chunk.Count;
            job?.Report(processed, imported, skipped, failed, pending);
        }

        var batch = new ImportBatchDocument
        {
            Id = batchId,
            BankId = bankId,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            Total = plan.Candidates.Count,
            Imported = imported,
            Skipped = skipped,
            Failed = failed,
            Pending = pending,
            BatchIssues = plan.BatchIssues.Select(ImportMapping.ToDocument).ToList(),
            // 明细写在 import_batch_items，批次文档只留汇总，避免撑爆 16MB 上限。
            Items = [],
        };

        await Batches.InsertOneAsync(batch, cancellationToken: cancellationToken);

        return new ImportRunResult(
            batchId,
            plan.Candidates.Count,
            imported,
            skipped,
            failed,
            pending,
            plan.BatchIssues,
            items);
    }

    public async Task<IReadOnlyList<ImportBatchSummaryDto>> ListBatchesAsync(
        string bankId,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var access = await RequireAccessAsync(bankId, userId, cancellationToken);

        var batches = await Batches
            .Find(x => x.BankId == bankId)
            .Sort(Builders<ImportBatchDocument>.Sort.Descending(x => x.CreatedAt))
            .Limit(30)
            .ToListAsync(cancellationToken);

        return batches.Select(ToSummary).ToList();
    }

    public async Task<ImportBatchDetailDto> GetBatchAsync(string batchId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var batch = await Batches.Find(x => x.Id == batchId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("导入批次不存在");
        await RequireAccessAsync(batch.BankId, userId, cancellationToken);

        var records = await BatchItems
            .Find(x => x.BatchId == batchId)
            .Sort(Builders<ImportBatchItemRecord>.Sort.Ascending(x => x.Index))
            .ToListAsync(cancellationToken);

        var items = records.Count > 0
            ? records.Select(record => record.ToItem()).ToList()
            : batch.Items;

        return new ImportBatchDetailDto(ToSummary(batch), items.Select(ToPreview).ToList());
    }

    public async Task<ImportApproveResultDto> ApproveAsync(string batchId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var batch = await Batches.Find(x => x.Id == batchId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("导入批次不存在");
        var access = await RequireAccessAsync(batch.BankId, userId, cancellationToken);
        if (!access.CanEdit)
        {
            throw new ForbiddenException("没有权限审核这个批次的题目");
        }

        var filter = Builders<QuestionDocument>.Filter.Eq("importBatchId", batchId)
                     & Builders<QuestionDocument>.Filter.Eq(x => x.Status, QuestionStatus.Pending)
                     & Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null);
        var update = Builders<QuestionDocument>.Update
            .Set(x => x.Status, QuestionStatus.Published)
            .Set(x => x.Unreviewed, false)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await Questions.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        batch.Pending = 0;
        await Batches.ReplaceOneAsync(x => x.Id == batch.Id, batch, cancellationToken: cancellationToken);

        return new ImportApproveResultDto((int)result.ModifiedCount, 0);
    }

    public async Task<ImportPromptDto> BuildPromptAsync(string bankId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var access = await RequireAccessAsync(bankId, userId, cancellationToken);
        var tags = await LoadBankTagsAsync(bankId, cancellationToken);
        var chapters = await LoadBankChaptersAsync(bankId, cancellationToken);
        var poolDocs = await poolStore.GetPoolsAsync(bankId, cancellationToken);
        var poolInfos = new List<ImportPoolPromptInfo>(poolDocs.Count);
        foreach (var pool in poolDocs)
        {
            var available = await poolStore.CountAvailableAsync(pool.Id, cancellationToken);
            poolInfos.Add(new ImportPoolPromptInfo(pool.Id, pool.Name, (int)available));
        }

        return new ImportPromptDto(
            access.Bank.Name,
            ImportPromptBuilder.Build(
                tags.OrderBy(tag => tag, StringComparer.Ordinal).ToList(),
                chapters,
                poolInfos),
            tags.Count);
    }

    private async Task<CachedImportPlan> BuildPlanAsync(string bankId, string payload, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var access = await RequireAccessAsync(bankId, userId, cancellationToken);
        if (!access.CanEdit)
        {
            throw new ForbiddenException("没有权限向该题库导入题目");
        }

        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
        {
            throw new ValidationFailedException("payload", "内容过大，一次最多 50MB");
        }

        var parsed = ImportParser.Parse(payload);
        var batchIssues = new List<ImportIssueDto>(parsed.Issues);
        if (parsed.Questions.Count > MaxQuestionsPerImport)
        {
            throw new ValidationFailedException("payload", $"一次最多导入 {MaxQuestionsPerImport} 道题");
        }

        var bankTags = await LoadBankTagsAsync(bankId, cancellationToken);
        var existingHashes = await LoadHashesAsync(bankId, cancellationToken);
        var seenHashes = new Dictionary<string, int>(StringComparer.Ordinal);

        var poolDocs = await poolStore.GetPoolsAsync(bankId, cancellationToken);
        var referencedPools = new Dictionary<string, OptionPoolDocument>(StringComparer.Ordinal);
        foreach (var question in parsed.Questions)
        {
            if (ReadPoolReference(question) is { } reference &&
                MatchPool(poolDocs, reference) is { } pool)
            {
                referencedPools.TryAdd(pool.Id, pool);
            }
        }

        var poolEntries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pool in referencedPools.Values)
        {
            var entries = await poolStore.GetEntriesAsync(pool.Id, false, cancellationToken);
            poolEntries[pool.Id] = entries.Select(entry => entry.Text).ToList();
        }

        var candidates = new List<ImportCandidate>(parsed.Questions.Count);
        for (var index = 0; index < parsed.Questions.Count; index++)
        {
            var candidate = ValidateOne(
                parsed.Questions[index],
                index,
                bankTags,
                existingHashes,
                poolDocs,
                poolEntries);
            if (!candidate.HasError && candidate.ContentHash.Length > 0)
            {
                if (seenHashes.TryGetValue(candidate.ContentHash, out var firstIndex))
                {
                    candidate.Duplicate = true;
                    candidate.Issues.Add(new ImportIssueDto(
                        ImportIssueCodes.Duplicate,
                        $"与本批第 {firstIndex + 1} 题重复",
                        ImportSeverity.Warning,
                        index));
                }
                else
                {
                    seenHashes[candidate.ContentHash] = index;
                }
            }

            candidates.Add(candidate);
        }

        batchIssues.AddRange(CheckAnswerPositionBias(candidates));
        return new CachedImportPlan(candidates, batchIssues);
    }

    private async Task<BankAccessDto> RequireAccessAsync(string bankId, string userId, CancellationToken cancellationToken)
    {
        var access = await banks.GetAccessAsync(bankId, userId, currentUser.ClassId, cancellationToken)
            ?? throw new NotFoundException("题库不存在");
        if (!access.CanView)
        {
            throw new NotFoundException("题库不存在");
        }

        return access;
    }

    private async Task<HashSet<string>> LoadBankTagsAsync(string bankId, CancellationToken cancellationToken)
    {
        var tags = await Questions
            .Find(x => x.BankId == bankId && x.DeletedAt == null)
            .Project(x => x.Tags)
            .ToListAsync(cancellationToken);

        return tags
            .SelectMany(list => list)
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<IReadOnlyList<IReadOnlyList<string>>> LoadBankChaptersAsync(
        string bankId,
        CancellationToken cancellationToken)
    {
        var paths = await Questions
            .Find(x => x.BankId == bankId && x.DeletedAt == null)
            .Project(x => x.ChapterPath)
            .ToListAsync(cancellationToken);

        return paths
            .Select(path => path
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .ToList())
            .Where(path => path.Count > 0)
            .GroupBy(path => string.Join('\u001f', path), StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<string>)group.First())
            .OrderBy(path => string.Join('\u001f', path), StringComparer.Ordinal)
            .ToList();
    }

    private async Task<HashSet<string>> LoadHashesAsync(string bankId, CancellationToken cancellationToken)
    {
        var hashes = await Questions
            .Find(x => x.BankId == bankId && x.DeletedAt == null)
            .Project(x => x.ContentHash)
            .ToListAsync(cancellationToken);

        return hashes.Where(hash => !string.IsNullOrEmpty(hash)).ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<ImportIssueDto> CheckAnswerPositionBias(IReadOnlyList<ImportCandidate> candidates)
    {
        var positions = candidates
            .Where(candidate => !candidate.HasError && candidate.Type == QuestionTypes.SingleChoice && candidate.Answer.Count == 1)
            .Select(candidate => candidate.Options.FindIndex(option => option.Key == candidate.Answer[0]))
            .Where(position => position >= 0)
            .ToList();

        if (positions.Count < 5)
        {
            return [];
        }

        var top = positions
            .GroupBy(position => position)
            .OrderByDescending(group => group.Count())
            .First();

        return top.Count() / (double)positions.Count > 0.6
            ? [new ImportIssueDto(
                ImportIssueCodes.AnswerPositionBias,
                $"有 {top.Count()}/{positions.Count} 道单选题的答案都落在第 {top.Key + 1} 个位置，建议打散",
                ImportSeverity.Warning)]
            : [];
    }

    private static ImportCandidate ValidateOne(
        JsonElement element,
        int index,
        IReadOnlySet<string> bankTags,
        IReadOnlySet<string> existingHashes,
        IReadOnlyList<OptionPoolDocument> pools,
        IReadOnlyDictionary<string, List<string>> poolEntries)
    {
        var candidate = new ImportCandidate { Index = index };

        void Error(string code, string message) =>
            candidate.Issues.Add(new ImportIssueDto(code, message, ImportSeverity.Error, index));

        void Warn(string code, string message) =>
            candidate.Issues.Add(new ImportIssueDto(code, message, ImportSeverity.Warning, index));

        if (element.ValueKind != JsonValueKind.Object)
        {
            Error(ImportIssueCodes.Schema, "题目必须是对象");
            return candidate;
        }

        candidate.ClientId = ReadString(element, "clientId");
        candidate.Type = NormalizeType(ReadString(element, "type"));
        if (candidate.Type is null)
        {
            Error(ImportIssueCodes.Schema, "题型必须是 single 或 multiple");
        }

        candidate.Stem = (ReadString(element, "stem") ?? "").Trim();
        if (candidate.Stem.Length == 0)
        {
            Error(ImportIssueCodes.EmptyStem, "题干不能为空");
        }
        else if (candidate.Stem.Length > 4000)
        {
            Error(ImportIssueCodes.Schema, "题干不能超过 4000 个字符");
        }

        var poolReference = ReadPoolReference(element);
        var pool = poolReference is null ? null : MatchPool(pools, poolReference);
        var poolCount = 0;

        if (poolReference is not null)
        {
            if (pool is null)
            {
                Error(
                    ImportIssueCodes.PoolNotFound,
                    poolReference.Name is { Length: > 0 } poolName
                        ? $"选项池不存在：{poolName}"
                        : poolReference.Id is { Length: > 0 } poolId
                            ? $"选项池不存在：{poolId}"
                            : "选项池不存在：题目没有写池子的 id 或名称");
            }
            else if (poolReference.Count is not (>= 1 and <= 9))
            {
                Error(ImportIssueCodes.PoolCount, $"选项池「{pool.Name}」的干扰项数量必须在 1 到 9 之间");
            }
            else
            {
                poolCount = poolReference.Count.Value;
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var fallbackIndex = 0;
        foreach (var option in ReadOptions(element))
        {
            var key = option.Key ?? Label(fallbackIndex);
            fallbackIndex += 1;
            var text = (option.Text ?? "").Trim();

            if (text.Length == 0)
            {
                Error(ImportIssueCodes.Schema, $"选项 {key} 的内容为空");
                continue;
            }

            if (!keys.Add(key))
            {
                Error(ImportIssueCodes.OptionDupKey, $"选项 key 重复：{key}");
                continue;
            }

            candidate.Options.Add(new OptionInput(key, text));
        }

        var minOptions = poolReference is null ? 2 : 1;
        if (candidate.Options.Count < minOptions || candidate.Options.Count > 10)
        {
            Error(ImportIssueCodes.Schema, poolReference is null
                ? "选项数量必须在 2 到 10 之间"
                : "使用选项池时，题目自身至少写 1 个正确答案选项，最多 10 个");
        }

        candidate.Answer.AddRange(ReadStringArray(element, "answer").Where(key => key.Length > 0));
        if (candidate.Answer.Count == 0)
        {
            Error(ImportIssueCodes.AnswerKeyMissing, "答案不能为空");
        }

        foreach (var key in candidate.Answer.Where(key => !keys.Contains(key)))
        {
            Error(ImportIssueCodes.AnswerKeyMissing, $"答案 key 不在选项中：{key}");
        }

        if (candidate.Type == QuestionTypes.SingleChoice && candidate.Answer.Count != 1)
        {
            Error(ImportIssueCodes.SingleAnswerCount, "单选题必须恰好 1 个答案");
        }

        if (candidate.Type == QuestionTypes.MultipleChoice)
        {
            if (candidate.Answer.Count < 2)
            {
                Error(ImportIssueCodes.MultipleAnswerCount, "多选题至少需要 2 个答案");
            }
            else if (candidate.Answer.Count >= candidate.Options.Count + poolCount)
            {
                Error(ImportIssueCodes.MultipleAnswerCount, "多选题的答案数量必须少于选项数");
            }
        }

        if (pool is not null && poolCount is >= 1 and <= 9)
        {
            var used = candidate.Options
                .Select(option => QuestionNormalizer.Normalize(option.Text))
                .ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<string> entries = poolEntries.TryGetValue(pool.Id, out var entryTexts)
                ? entryTexts
                : [];
            var usable = entries.Count(text => used.Add(QuestionNormalizer.Normalize(text)));

            if (usable < poolCount + 1)
            {
                Error(
                    ImportIssueCodes.PoolCapacity,
                    $"选项池「{pool.Name}」至少需要 {poolCount + 1} 条可用干扰项，现在只有 {usable} 条");
            }

            if (candidate.Options.Count + poolCount > 10)
            {
                Error(ImportIssueCodes.Schema, "题目自身选项加上选项池抽取的干扰项不能超过 10 个");
            }

            candidate.DistractorRule = new DistractorRuleDto(pool.Id, poolCount);
            candidate.PoolName = pool.Name;
            candidate.PoolCount = poolCount;
        }

        foreach (var tag in ReadStringArray(element, "tags"))
        {
            if (tag.Length == 0 || candidate.Tags.Contains(tag, StringComparer.Ordinal))
            {
                continue;
            }

            candidate.Tags.Add(tag);
            if (bankTags.Count > 0 && !bankTags.Contains(tag))
            {
                Warn(ImportIssueCodes.UnknownTag, $"标签「{tag}」不在题库已有标签里");
            }
        }

        if (candidate.Tags.Count > 10)
        {
            Error(ImportIssueCodes.Schema, "标签最多 10 个");
        }

        candidate.ChapterPath.AddRange(ChapterPaths.Normalize(ReadStringArray(element, "chapterPath")));
        if (ChapterPaths.FindProblem(candidate.ChapterPath) is { } chapterProblem)
        {
            Error(ImportIssueCodes.Schema, chapterProblem);
        }

        var difficulty = ReadInt(element, "difficulty") ?? 3;
        if (difficulty is < 1 or > 5)
        {
            Warn(ImportIssueCodes.Difficulty, "难度超出 1–5，按 3 处理");
            difficulty = 3;
        }

        candidate.Difficulty = difficulty;
        candidate.Shuffle = ReadShuffle(element);
        candidate.Source = ReadString(element, "source");
        candidate.Explanation = (ReadString(element, "explanation") ?? "").Trim();

        if (candidate.Explanation.Length == 0)
        {
            Warn(ImportIssueCodes.MissingExplanation, "没有写解析");
        }

        if (HasBrokenLatex(candidate.Stem) ||
            HasBrokenLatex(candidate.Explanation) ||
            candidate.Options.Any(option => HasBrokenLatex(option.Text)))
        {
            Warn(ImportIssueCodes.BrokenLatex, "有未闭合的 $ 或代码围栏");
        }

        CheckOptionLengthBias(candidate, Warn);

        candidate.ContentHash = QuestionNormalizer.ComputeContentHash(
            candidate.Stem,
            candidate.Options.Select(option => option.Text));

        if (!candidate.HasError)
        {
            candidate.Duplicate = existingHashes.Contains(candidate.ContentHash);
            if (candidate.Duplicate)
            {
                Warn(ImportIssueCodes.Duplicate, "题库里已有相同题目，默认会跳过");
            }
        }

        return candidate;
    }

    private static void CheckOptionLengthBias(
        ImportCandidate candidate,
        Action<string, string> warn)
    {
        if (candidate.Type != QuestionTypes.SingleChoice ||
            candidate.Answer.Count != 1 ||
            candidate.Options.Count < 3)
        {
            return;
        }

        var correct = candidate.Options.FirstOrDefault(option => option.Key == candidate.Answer[0]);
        if (correct is null)
        {
            return;
        }

        var others = candidate.Options
            .Where(option => option.Key != correct.Key)
            .Select(option => option.Text.Length)
            .OrderBy(length => length)
            .ToArray();

        var median = others[others.Length / 2];
        if (correct.Text.Length >= 12 && correct.Text.Length > median * 1.6)
        {
            warn(ImportIssueCodes.OptionLengthBias, "正确选项明显比其他选项长，容易被猜出来");
        }
    }

    private static bool HasBrokenLatex(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        var dollars = text.Count(character => character == '$');
        var fences = CountOccurrences(text, "```");
        return dollars % 2 != 0 || fences % 2 != 0;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count += 1;
            index += value.Length;
        }

        return count;
    }

    private static string? NormalizeType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "single" or "single_choice" or "singlechoice" or "单选" => QuestionTypes.SingleChoice,
        "multiple" or "multiple_choice" or "multiplechoice" or "多选" => QuestionTypes.MultipleChoice,
        _ => null,
    };

    private static string ReadShuffle(JsonElement element)
    {
        if (!element.TryGetProperty("shuffle", out var value))
        {
            return ShuffleMode.Follow;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean() ? ShuffleMode.Always : ShuffleMode.Never;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
        return text is ShuffleMode.Always or ShuffleMode.Never or ShuffleMode.Follow
            ? text
            : ShuffleMode.Follow;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static IEnumerable<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString()?.Trim();
            return string.IsNullOrEmpty(single) ? [] : [single];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? "")
            .Where(item => item.Length > 0)
            .ToList();
    }

    private static IEnumerable<(string? Key, string? Text)> ReadOptions(JsonElement element)
    {
        if (!element.TryGetProperty("options", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<(string?, string?)>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                result.Add((null, item.GetString()));
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                result.Add((ReadString(item, "key")?.Trim(), ReadString(item, "text")));
            }
        }

        return result;
    }

    private static string Label(int index)
    {
        var label = string.Empty;
        var value = index;
        do
        {
            label = (char)('A' + value % 26) + label;
            value = value / 26 - 1;
        }
        while (value >= 0);

        return label;
    }

    private sealed record PoolReference(string? Id, string? Name, int? Count);

    private static PoolReference? ReadPoolReference(JsonElement element)
    {
        if (!element.TryGetProperty("pool", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return new PoolReference(null, EmptyToNull(value.GetString()), null);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new PoolReference(
            EmptyToNull(ReadString(value, "id")),
            EmptyToNull(ReadString(value, "name")),
            ReadInt(value, "count"));
    }

    private static OptionPoolDocument? MatchPool(
        IReadOnlyList<OptionPoolDocument> pools,
        PoolReference reference)
    {
        if (reference.Id is { } id)
        {
            var byId = pools.FirstOrDefault(pool => string.Equals(pool.Id, id, StringComparison.Ordinal));
            if (byId is not null)
            {
                return byId;
            }
        }

        return reference.Name is { } name
            ? pools.FirstOrDefault(pool => string.Equals(pool.Name.Trim(), name, StringComparison.Ordinal))
            : null;
    }

    private static string? EmptyToNull(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ImportBatchItemDocument ToDocument(ImportCandidate candidate, string status, string? questionId) => new()
    {
        QuestionId = questionId,
        ClientId = candidate.ClientId,
        Stem = candidate.Stem,
        ChapterPath = candidate.ChapterPath.ToList(),
        Status = status,
        Issues = candidate.Issues.Select(ImportMapping.ToDocument).ToList(),
    };

    private static ImportQuestionPreviewDto ToPreview(ImportBatchItemDocument document) => new(
        document.Issues.FirstOrDefault()?.Index ?? 0,
        document.ClientId,
        document.Stem,
        document.Status,
        document.Issues.Select(ImportMapping.ToDto).ToList(),
        document.ChapterPath.ToList());

    private static ImportBatchSummaryDto ToSummary(ImportBatchDocument batch) => new(
        batch.Id,
        batch.BankId,
        batch.CreatedBy,
        batch.CreatedAt,
        batch.Total,
        batch.Imported,
        batch.Skipped,
        batch.Failed,
        batch.Pending);

    private sealed record ImportRunResult(
        string BatchId,
        int Total,
        int Imported,
        int Skipped,
        int Failed,
        int Pending,
        IReadOnlyList<ImportIssueDto> BatchIssues,
        List<ImportBatchItemDocument> Items);
}

internal sealed class ImportCandidate
{
    public int Index { get; init; }

    public string? ClientId { get; set; }

    public string Stem { get; set; } = "";

    public string? Type { get; set; }

    public List<OptionInput> Options { get; } = [];

    public List<string> Answer { get; } = [];

    public List<string> Tags { get; } = [];

    public List<string> ChapterPath { get; } = [];

    public int Difficulty { get; set; } = 3;

    public string Shuffle { get; set; } = ShuffleMode.Follow;

    public string? Source { get; set; }

    public string Explanation { get; set; } = "";

    public string ContentHash { get; set; } = "";

    public DistractorRuleDto? DistractorRule { get; set; }

    public string? PoolName { get; set; }

    public int? PoolCount { get; set; }

    public bool Duplicate { get; set; }

    public List<ImportIssueDto> Issues { get; } = [];

    public bool HasError => Issues.Any(issue => issue.Severity == ImportSeverity.Error);

    public string Status => HasError ? "error" : Issues.Count > 0 ? "warning" : "valid";

    public ImportQuestionPreviewDto ToPreview() =>
        new(Index, ClientId, Stem, Status, Issues, ChapterPath.ToList(), PoolName, PoolCount);

    public SaveQuestionRequest ToRequest() => new(
        Type ?? QuestionTypes.SingleChoice,
        Stem,
        Options.ToList(),
        Answer.ToList(),
        Shuffle,
        Tags.ToList(),
        Difficulty,
        Source,
        Explanation,
        DistractorRule: DistractorRule,
        ChapterPath: ChapterPath.ToList());
}
