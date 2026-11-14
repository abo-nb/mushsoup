using System.Text.Json;
using Banks.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Identity.Contracts;
using MongoDB.Bson;
using MongoDB.Driver;
using Practice.Contracts;
using Questions.Contracts;
using Scheduling.Contracts;

namespace Practice;

sealed class PracticeService(
    MongoDatabaseProvider database,
    ICurrentUser currentUser,
    IBankDirectory banks,
    IQuestionCatalog questions,
    IUserDirectory users,
    ICardFactory cards,
    IQuestionGrader grader,
    IReviewScheduler scheduler) : IPracticeStats
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    /// <summary>无限模式下，连续答对这么多次就当作练熟，移出本轮队列。</summary>
    private const int EndlessMasteryStreak = 2;

    /// <summary>答错的题至少隔这么多张卡再出现。</summary>
    private const int RequeueGap = 2;

    private static readonly JsonSerializerOptions CardJsonOptions = new(JsonSerializerDefaults.Web);

    private IMongoCollection<PracticeSessionDocument> Sessions =>
        database.Collection<PracticeSessionDocument>(PracticeCollections.Sessions);

    private IMongoCollection<ReviewEventDocument> ReviewEvents =>
        database.Collection<ReviewEventDocument>(PracticeCollections.ReviewEvents);

    public static async Task EnsureIndexesAsync(MongoDatabaseProvider database, CancellationToken cancellationToken)
    {
        var sessions = database.Collection<PracticeSessionDocument>(PracticeCollections.Sessions);
        await sessions.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<PracticeSessionDocument>(
                Builders<PracticeSessionDocument>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.StartedAt)),
        ], cancellationToken);

        var events = database.Collection<ReviewEventDocument>(PracticeCollections.ReviewEvents);
        await events.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ReviewEventDocument>(
                Builders<ReviewEventDocument>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.QuestionId)),
            new CreateIndexModel<ReviewEventDocument>(
                Builders<ReviewEventDocument>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.OccurredAt)),
        ], cancellationToken);
    }

    public async Task<IReadOnlyList<UserPracticeStatsDto>> GetUserStatsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var buckets = await ReviewEvents
            .Aggregate()
            .Match(Builders<ReviewEventDocument>.Filter.In(x => x.UserId, ids))
            .Group(new BsonDocument
            {
                { "_id", "$UserId" },
                { "answered", new BsonDocument("$sum", 1) },
                { "correct", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { "$Correct", 1, 0 })) },
            })
            .ToListAsync(cancellationToken);

        var byUser = buckets.ToDictionary(
            bucket => bucket["_id"].AsString,
            bucket => (Answered: bucket["answered"].ToInt32(), Correct: bucket["correct"].ToInt32()),
            StringComparer.Ordinal);

        return ids
            .Select(id => byUser.TryGetValue(id, out var stats)
                ? new UserPracticeStatsDto(id, stats.Answered, stats.Correct)
                : new UserPracticeStatsDto(id, 0, 0))
            .ToList();
    }

    public async Task<SessionDto> StartSessionAsync(string bankId, StartSessionRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var access = await banks.GetAccessAsync(bankId, userId, currentUser.ClassId, cancellationToken)
            ?? throw new NotFoundException("题库不存在");
        if (!access.CanView)
        {
            throw new ForbiddenException("没有权限查看这个题库");
        }

        var mode = string.IsNullOrWhiteSpace(request.Mode)
            ? SessionMode.Sequence
            : request.Mode.Trim().ToLowerInvariant();
        if (!SessionMode.IsKnown(mode))
        {
            throw new ValidationFailedException("mode", "不支持的练习模式");
        }

        var published = (await questions.GetByBankAsync(bankId, cancellationToken))
            .Where(question => question.Status == QuestionStatus.Published)
            .ToList();
        if (published.Count == 0)
        {
            throw new ValidationFailedException("bankId", "题库里还没有题目");
        }

        var chapterFilters = (request.Chapters ?? [])
            .Select(FilterSegments)
            .Where(segments => segments.Count > 0)
            .ToList();
        if (chapterFilters.Count > 0)
        {
            published = published
                .Where(question => chapterFilters.Any(filter => IsPrefix(filter, question.ChapterPath)))
                .ToList();
            if (published.Count == 0)
            {
                throw new ValidationFailedException("chapters", "所选章节下没有题目");
            }
        }

        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);
        var ordered = mode switch
        {
            SessionMode.Endless => await BuildEndlessQueueAsync(userId, published, cancellationToken),
            SessionMode.Daily => await BuildDailyQueueAsync(userId, published, limit, cancellationToken),
            _ => published.OrderBy(question => question.UpdatedAt).Take(limit).Select(question => question.Id).ToList(),
        };

        var session = new PracticeSessionDocument
        {
            Id = Ids.New(),
            UserId = userId,
            BankId = bankId,
            BankName = access.Bank.Name,
            Mode = mode,
            QuestionIds = ordered.Distinct().ToList(),
            Queue = ordered,
            StartedAt = DateTime.UtcNow,
        };
        await Sessions.InsertOneAsync(session, cancellationToken: cancellationToken);
        return ToDto(session);
    }

    public async Task<SessionDto> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        ToDto(await GetOwnedAsync(sessionId, cancellationToken));

    public async Task<NextCardResponse> GetNextAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await GetOwnedAsync(sessionId, cancellationToken);
        if (session.FinishedAt is not null)
        {
            return new NextCardResponse(ToDto(session), null, session.Served.Count);
        }

        if (session.CurrentQuestionId is null)
        {
            if (session.Queue.Count == 0)
            {
                await MarkFinishedAsync(session, cancellationToken);
                return new NextCardResponse(ToDto(session), null, session.Served.Count);
            }

            session.CurrentQuestionId = session.Queue[0];
            session.Queue.RemoveAt(0);
            session.Served.Add(session.CurrentQuestionId);
        }

        var question = await questions.GetAsync(session.CurrentQuestionId, cancellationToken)
            ?? throw new NotFoundException("题目不存在");
        var card = await BuildCardAsync(session, question, cancellationToken);
        session.CurrentCardJson = JsonSerializer.Serialize(card, CardJsonOptions);
        await SaveAsync(session, cancellationToken);

        return new NextCardResponse(ToDto(session), card, session.Served.Count);
    }

    public async Task<AnswerFeedbackDto> SubmitAnswerAsync(string sessionId, SubmitAnswerRequest request, CancellationToken cancellationToken)
    {
        var session = await GetOwnedAsync(sessionId, cancellationToken);
        if (session.FinishedAt is not null)
        {
            throw new ConflictException("这轮练习已经结束");
        }

        if (session.CurrentQuestionId is null)
        {
            throw new ConflictException("还没有发题");
        }

        if (!string.Equals(request.QuestionId, session.CurrentQuestionId, StringComparison.Ordinal))
        {
            throw new ValidationFailedException("questionId", "提交的题目不是当前题目");
        }

        var question = await questions.GetAsync(session.CurrentQuestionId, cancellationToken)
            ?? throw new NotFoundException("题目不存在");
        var selectedKeys = request.SelectedKeys ?? [];
        var grade = grader.Grade(question, selectedKeys);
        var card = await ResolveCurrentCardAsync(session, question, cancellationToken);
        var rating = Rate(grade.Correct, request.ElapsedMs);
        var now = DateTime.UtcNow;

        await ReviewEvents.InsertOneAsync(new ReviewEventDocument
        {
            Id = Ids.New(),
            UserId = session.UserId,
            QuestionId = question.Id,
            SessionId = session.Id,
            SelectedKeys = [.. selectedKeys],
            Correct = grade.Correct,
            Score = grade.Score,
            Rating = rating,
            ElapsedMs = request.ElapsedMs,
            Seed = card.Seed,
            CardJson = JsonSerializer.Serialize(card, CardJsonOptions),
            OccurredAt = now,
        }, cancellationToken: cancellationToken);

        await scheduler.ReviewAsync(session.UserId, question.Id, rating, cancellationToken);

        session.CurrentQuestionId = null;
        session.CurrentCardJson = null;
        if (grade.Correct)
        {
            session.Correct += 1;
        }
        else
        {
            session.Wrong += 1;
        }

        if (session.Mode == SessionMode.Endless)
        {
            // 只有稳稳答对才累计连击；慢或犹豫（hard）不算「熟」。
            var confident = grade.Correct && rating is ReviewRating.Good or ReviewRating.Easy;
            var streak = confident ? session.Streaks.GetValueOrDefault(question.Id) + 1 : 0;
            session.Streaks[question.Id] = streak;

            if (streak >= EndlessMasteryStreak)
            {
                if (!session.Mastered.Contains(question.Id))
                {
                    session.Mastered.Add(question.Id);
                }
            }
            else
            {
                session.Queue.Insert(Math.Min(session.Queue.Count, RequeueGap), question.Id);
            }
        }

        if (session.Queue.Count == 0)
        {
            session.FinishedAt = now;
        }

        await SaveAsync(session, cancellationToken);

        return new AnswerFeedbackDto(
            grade.Correct,
            grade.Score,
            question.Stem,
            BuildFeedbackOptions(card, question, selectedKeys),
            question.Explanation,
            rating,
            session.Served.Count,
            session.Queue.Count == 0,
            new SessionStatsDto(session.Correct, session.Wrong),
            ToDto(session));
    }

    public async Task<SessionDto> FinishAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await GetOwnedAsync(sessionId, cancellationToken);
        if (session.FinishedAt is null)
        {
            await MarkFinishedAsync(session, cancellationToken);
        }

        return ToDto(session);
    }

    private async Task<List<string>> BuildEndlessQueueAsync(
        string userId,
        List<QuestionDto> published,
        CancellationToken cancellationToken)
    {
        var states = await scheduler.GetManyAsync(userId, published.Select(question => question.Id), cancellationToken);
        var pool = published.Where(question => !IsMastered(states, question.Id)).ToList();
        if (pool.Count == 0)
        {
            pool = published;
        }

        return pool
            .OrderBy(question => StateRank(states, question.Id))
            .ThenBy(question => question.UpdatedAt)
            .Select(question => question.Id)
            .ToList();
    }

    private async Task<List<string>> BuildDailyQueueAsync(
        string userId,
        List<QuestionDto> published,
        int limit,
        CancellationToken cancellationToken)
    {
        var states = await scheduler.GetManyAsync(userId, published.Select(question => question.Id), cancellationToken);
        var now = DateTime.UtcNow;

        var due = published
            .Where(question => states.TryGetValue(question.Id, out var state) && state.DueAt <= now)
            .OrderBy(question => states[question.Id].DueAt)
            .Select(question => question.Id);

        var fresh = published
            .Where(question => !states.ContainsKey(question.Id))
            .OrderBy(question => question.UpdatedAt)
            .Select(question => question.Id);

        var ahead = published
            .Where(question => states.TryGetValue(question.Id, out var state) && state.DueAt > now)
            .OrderBy(question => states[question.Id].DueAt)
            .Select(question => question.Id);

        var queue = due.Concat(fresh).Take(limit).ToList();

        if (queue.Count < limit)
        {
            // 维护抽样：从还没到期的题里抽一小部分，避免「掌握了就再也不见」。
            var maintenance = ahead.Take(Math.Max(1, limit / 10));
            queue.AddRange(maintenance.Where(id => !queue.Contains(id)));
        }

        if (queue.Count == 0)
        {
            // 全部没到期，但总得让人有题可做。
            queue.AddRange(ahead.Take(limit));
        }

        return queue.Take(limit).ToList();
    }

    private async Task<PracticeSessionDocument> GetOwnedAsync(string sessionId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var session = await Sessions
            .Find(x => x.Id == sessionId && x.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);
        return session ?? throw new NotFoundException("会话不存在");
    }

    private Task SaveAsync(PracticeSessionDocument session, CancellationToken cancellationToken) =>
        Sessions.ReplaceOneAsync(x => x.Id == session.Id, session, cancellationToken: cancellationToken);

    private async Task MarkFinishedAsync(PracticeSessionDocument session, CancellationToken cancellationToken)
    {
        session.FinishedAt = DateTime.UtcNow;
        await SaveAsync(session, cancellationToken);
    }

    /// <summary>优先用发卡时的快照判分，避免池子在答题过程中变化导致选项对不上。</summary>
    private async Task<CardDto> ResolveCurrentCardAsync(
        PracticeSessionDocument session,
        QuestionDto question,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(session.CurrentCardJson))
        {
            var stored = JsonSerializer.Deserialize<CardDto>(session.CurrentCardJson, CardJsonOptions);
            if (stored is not null)
            {
                return stored;
            }
        }

        return await BuildCardAsync(session, question, cancellationToken);
    }

    private async Task<CardDto> BuildCardAsync(PracticeSessionDocument session, QuestionDto question, CancellationToken cancellationToken)
    {
        var attemptNo = await CountAttemptsAsync(session.UserId, question.Id, cancellationToken);
        var shuffle = await ResolveShuffleAsync(session, question, cancellationToken);
        return await cards.BuildAsync(question, session.UserId, (int)attemptNo, shuffle, cancellationToken);
    }

    private Task<long> CountAttemptsAsync(string userId, string questionId, CancellationToken cancellationToken) =>
        ReviewEvents.CountDocumentsAsync(
            x => x.UserId == userId && x.QuestionId == questionId,
            cancellationToken: cancellationToken);

    private async Task<bool> ResolveShuffleAsync(PracticeSessionDocument session, QuestionDto question, CancellationToken cancellationToken)
    {
        var prefs = await users.GetPrefsAsync(session.UserId, cancellationToken) ?? UserPrefsDto.Default;
        if (prefs.ShuffleOptions == ShuffleMode.Always)
        {
            return true;
        }

        if (prefs.ShuffleOptions == ShuffleMode.Never)
        {
            return false;
        }

        if (question.Shuffle == ShuffleMode.Always)
        {
            return true;
        }

        if (question.Shuffle == ShuffleMode.Never)
        {
            return false;
        }

        var access = await banks.GetAccessAsync(session.BankId, session.UserId, currentUser.ClassId, cancellationToken);
        return access?.Bank.Settings.DefaultShuffleOptions ?? BankSettingsDto.Default.DefaultShuffleOptions;
    }

    private static bool IsMastered(IReadOnlyDictionary<string, ReviewStateDto> states, string questionId) =>
        states.TryGetValue(questionId, out var state) && state.State == MemoryState.Mastered;

    private static List<string> FilterSegments(IReadOnlyList<string>? path) =>
        path is null
            ? []
            : path
                .Select(segment => segment?.Trim() ?? "")
                .Where(segment => segment.Length > 0)
                .ToList();

    /// <summary>选中的章节路径要求是题目章节路径的前缀，空层在比较时忽略。</summary>
    private static bool IsPrefix(IReadOnlyList<string> selected, IReadOnlyList<string> actual)
    {
        if (selected.Count == 0)
        {
            return false;
        }

        var depth = 0;
        foreach (var raw in actual)
        {
            var segment = raw?.Trim() ?? "";
            if (segment.Length == 0)
            {
                continue;
            }

            if (depth >= selected.Count)
            {
                return true;
            }

            if (!string.Equals(selected[depth], segment, StringComparison.Ordinal))
            {
                return false;
            }

            depth += 1;
        }

        return depth == selected.Count;
    }

    private static int StateRank(IReadOnlyDictionary<string, ReviewStateDto> states, string questionId)
    {
        if (!states.TryGetValue(questionId, out var state))
        {
            return 2;
        }

        return state.State switch
        {
            MemoryState.Learning => 0,
            MemoryState.Review => 1,
            MemoryState.Mastered => 3,
            _ => 2,
        };
    }

    private static string Rate(bool correct, long elapsedMs) =>
        !correct ? ReviewRating.Again
        : elapsedMs > 30_000 ? ReviewRating.Hard
        : elapsedMs < 8_000 ? ReviewRating.Easy
        : ReviewRating.Good;

    /// <summary>按「发出去的那张卡」生成反馈，池子抽出来的干扰项也能正确标红。</summary>
    private static IReadOnlyList<FeedbackOptionDto> BuildFeedbackOptions(
        CardDto card,
        QuestionDto question,
        IReadOnlyCollection<string> selectedKeys)
    {
        var correctKeys = question.Options
            .Where(option => option.Correct)
            .Select(option => option.Key)
            .ToHashSet(StringComparer.Ordinal);
        var selected = selectedKeys.ToHashSet(StringComparer.Ordinal);

        return card.Options.Select(option =>
        {
            var isCorrect = correctKeys.Contains(option.Key);
            var isSelected = selected.Contains(option.Key);
            var state = isCorrect
                ? isSelected ? "hit" : "missed"
                : isSelected ? "wrong" : "neutral";
            return new FeedbackOptionDto(option.Key, option.Text, state);
        }).ToList();
    }

    private static SessionDto ToDto(PracticeSessionDocument session) => new(
        session.Id,
        session.BankId,
        session.BankName,
        session.Mode,
        session.QuestionIds.Count,
        session.Correct + session.Wrong,
        session.Mastered.Count,
        session.Queue.Count + (session.CurrentQuestionId is null ? 0 : 1),
        session.StartedAt,
        session.FinishedAt,
        new SessionStatsDto(session.Correct, session.Wrong));
}
