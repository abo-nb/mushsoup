using BuildingBlocks.Infrastructure;
using MongoDB.Driver;
using Scheduling.Contracts;

namespace Scheduling;

/// <summary>
/// 记忆调度。每种算法各存一份状态（键是「用户 + 题目 + 算法」），互不污染：
/// sm2 走间隔复习，fsrs 按 FSRS-5 算稳定度和间隔，leitner 是固定档位的盒子，
/// speed 答对一次就记为已掌握。
/// </summary>
public sealed class ReviewScheduler(MongoDatabaseProvider database, SchedulingOptions options) : IReviewScheduler
{
    private const double MinEase = 1.3;

    private IMongoCollection<ReviewStateDocument> States =>
        database.Collection<ReviewStateDocument>(SchedulingCollections.ReviewStates);

    public async Task<ScheduleResult> ReviewAsync(
        string userId,
        string questionId,
        string rating,
        string algorithm,
        CancellationToken cancellationToken = default)
    {
        var effectiveRating = ReviewRating.IsKnown(rating) ? rating : ReviewRating.Good;
        var effectiveAlgorithm = ReviewAlgorithm.IsKnown(algorithm) ? algorithm : ReviewAlgorithm.Sm2;
        var now = DateTime.UtcNow;

        var state = await States
            .Find(StateFilter(userId, questionId, effectiveAlgorithm))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new ReviewStateDocument
            {
                Id = StateId(userId, questionId, effectiveAlgorithm),
                UserId = userId,
                QuestionId = questionId,
                Algorithm = effectiveAlgorithm,
                Ease = options.InitialEase,
                State = MemoryState.New,
            };

        var wasMastered = state.State == MemoryState.Mastered;
        switch (effectiveAlgorithm)
        {
            case ReviewAlgorithm.Speed:
                ApplySpeed(state, effectiveRating, now);
                break;
            case ReviewAlgorithm.Fsrs:
                ApplyFsrs(state, effectiveRating, now);
                break;
            case ReviewAlgorithm.Leitner:
                ApplyLeitner(state, effectiveRating, now);
                break;
            default:
                ApplySm2(state, effectiveRating, now);
                break;
        }

        state.Algorithm = effectiveAlgorithm;
        state.LastRating = effectiveRating;
        state.LastReviewedAt = now;
        state.UpdatedAt = now;

        await States.ReplaceOneAsync(
            x => x.Id == state.Id,
            state,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);

        return new ScheduleResult(ToDto(state), effectiveRating, !wasMastered && state.State == MemoryState.Mastered, state.DueAt);
    }

    public async Task<ReviewStateDto?> GetAsync(
        string userId,
        string questionId,
        string algorithm,
        CancellationToken cancellationToken = default)
    {
        var effectiveAlgorithm = ReviewAlgorithm.IsKnown(algorithm) ? algorithm : ReviewAlgorithm.Sm2;
        var state = await States
            .Find(StateFilter(userId, questionId, effectiveAlgorithm))
            .FirstOrDefaultAsync(cancellationToken);
        return state is null ? null : ToDto(state);
    }

    public async Task<IReadOnlyDictionary<string, ReviewStateDto>> GetManyAsync(
        string userId,
        IEnumerable<string> questionIds,
        string algorithm,
        CancellationToken cancellationToken = default)
    {
        var ids = questionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, ReviewStateDto>();
        }

        var effectiveAlgorithm = ReviewAlgorithm.IsKnown(algorithm) ? algorithm : ReviewAlgorithm.Sm2;
        var builder = Builders<ReviewStateDocument>.Filter;
        var states = await States
            .Find(builder.Eq(x => x.UserId, userId)
                & AlgorithmFilter(builder, effectiveAlgorithm)
                & builder.In(x => x.QuestionId, ids))
            .ToListAsync(cancellationToken);

        return states.ToDictionary(x => x.QuestionId, ToDto);
    }

    private static FilterDefinition<ReviewStateDocument> StateFilter(
        string userId,
        string questionId,
        string algorithm)
    {
        var builder = Builders<ReviewStateDocument>.Filter;
        return builder.Eq(x => x.UserId, userId)
            & builder.Eq(x => x.QuestionId, questionId)
            & AlgorithmFilter(builder, algorithm);
    }

    /// <summary>升级前写入的记录没有 Algorithm 字段，按 SM-2 认领，避免进度看起来丢了。</summary>
    private static FilterDefinition<ReviewStateDocument> AlgorithmFilter(
        FilterDefinitionBuilder<ReviewStateDocument> builder,
        string algorithm) =>
        algorithm == ReviewAlgorithm.Sm2
            ? (builder.Eq(x => x.Algorithm, ReviewAlgorithm.Sm2) | builder.Exists(x => x.Algorithm, false))
            : builder.Eq(x => x.Algorithm, algorithm);

    private static string StateId(string userId, string questionId, string algorithm) =>
        $"{userId}:{questionId}:{algorithm}";

    /// <summary>速度优先：答对一次就是学会；答错的题立刻可再次出现。</summary>
    private static void ApplySpeed(ReviewStateDocument state, string rating, DateTime now)
    {
        if (QualityOf(rating) < 3)
        {
            state.Reps = 0;
            state.Lapses += 1;
            state.IntervalDays = 0;
            state.DueAt = now;
            state.State = MemoryState.Learning;
            return;
        }

        state.Reps += 1;
        state.IntervalDays = 0;
        // 学会的题不再进入日常队列；DueAt 只是留个长期时间戳。
        state.DueAt = now.AddYears(10);
        state.State = MemoryState.Mastered;
    }

    /// <summary>
    /// FSRS-5：新卡先按初始稳定度排一天，之后间隔完全由稳定度和目标保持率决定。
    /// 答错的题回到学习状态，由调用方在当天重排。
    /// </summary>
    private void ApplyFsrs(ReviewStateDocument state, string rating, DateTime now)
    {
        var isNew = state.Stability <= 0;
        var elapsedDays = state.LastReviewedAt is { } last
            ? Math.Max(0, (now - last).TotalDays)
            : 0;

        var result = FsrsSchedule.Next(
            state.Stability,
            state.Difficulty,
            rating,
            isNew ? 0 : elapsedDays,
            options.TargetRetention);

        state.Stability = result.Stability;
        state.Difficulty = result.Difficulty;

        if (result.Lapsed)
        {
            state.Reps = 0;
            state.Lapses += 1;
            state.IntervalDays = 0;
            state.DueAt = now.AddMinutes(options.RelearnMinutes);
            state.State = MemoryState.Learning;
            return;
        }

        state.Reps += 1;
        var interval = Math.Max(1, result.IntervalDays);
        if (isNew)
        {
            interval = Math.Min(interval, Math.Max(1, options.NewCardIntervalDays));
        }

        state.IntervalDays = interval;
        state.DueAt = now.AddDays(interval);
        state.State = interval >= options.MasteredIntervalDays ? MemoryState.Mastered : MemoryState.Review;
    }

    /// <summary>Leitner：答对进一格，答错回第一格；格子对应的间隔是固定的。</summary>
    private void ApplyLeitner(ReviewStateDocument state, string rating, DateTime now)
    {
        var result = LeitnerSchedule.Next(state.Box, rating);
        state.Box = result.Box;

        if (result.Lapsed)
        {
            state.Reps = 0;
            state.Lapses += 1;
            state.IntervalDays = 0;
            state.DueAt = now.AddMinutes(options.RelearnMinutes);
            state.State = MemoryState.Learning;
            return;
        }

        state.Reps += 1;
        state.IntervalDays = Math.Max(1, result.IntervalDays);
        state.DueAt = now.AddDays(state.IntervalDays);
        state.State = result.Box >= LeitnerSchedule.MaxBox ? MemoryState.Mastered : MemoryState.Review;
    }

    /// <summary>
    /// SM-2 + Anki 式学习步：新卡和重学卡先走两步短间隔，毕业后再按 1 → 6 → interval × ease 拉长。
    /// </summary>
    private void ApplySm2(ReviewStateDocument state, string rating, DateTime now)
    {
        var quality = QualityOf(rating);

        state.Ease = Math.Max(MinEase, state.Ease + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02)));

        if (quality < 3)
        {
            if (state.State is MemoryState.Review or MemoryState.Mastered)
            {
                // 只有从复习状态掉下来时才记原间隔；学习步里连错不清掉它。
                state.PreviousIntervalDays = state.IntervalDays;
            }

            state.Reps = 0;
            state.Lapses += 1;
            state.LearningStep = 0;
            state.IntervalDays = 0;
            state.DueAt = now.AddMinutes(options.RelearnMinutes);
            state.State = MemoryState.Learning;
            return;
        }

        if (state.State is not (MemoryState.Review or MemoryState.Mastered))
        {
            var steps = options.LearningSteps ?? [];
            state.LearningStep += 1;
            if (state.LearningStep < steps.Length)
            {
                state.IntervalDays = 0;
                state.DueAt = now.AddMinutes(Math.Max(1, steps[state.LearningStep - 1]));
                state.State = MemoryState.Learning;
                return;
            }

            // 毕业：新卡给一天，重学卡恢复到原来间隔的一半。
            state.Reps = 1;
            state.IntervalDays = Math.Max(1, (int)Math.Round(state.PreviousIntervalDays / 2));
            state.LearningStep = 0;
            state.PreviousIntervalDays = 0;
            state.DueAt = now.AddDays(state.IntervalDays);
            state.State = MemoryState.Review;
            return;
        }

        var previousInterval = state.IntervalDays;
        state.Reps += 1;

        var nextInterval = state.Reps switch
        {
            1 => 1,
            2 => 6,
            _ => Math.Max(1, (int)Math.Round(previousInterval * state.Ease)),
        };

        if (rating == ReviewRating.Hard && state.Reps > 2)
        {
            nextInterval = Math.Max(1, (int)Math.Round(previousInterval * 1.2));
        }

        if (rating == ReviewRating.Easy)
        {
            nextInterval = Math.Max(nextInterval + 1, (int)Math.Round(nextInterval * 1.3));
        }

        state.IntervalDays = nextInterval;
        state.DueAt = now.AddDays(nextInterval);
        state.State = nextInterval >= options.MasteredIntervalDays && state.Reps >= options.MasteredReps
            ? MemoryState.Mastered
            : MemoryState.Review;
    }

    private static int QualityOf(string rating) => rating switch
    {
        ReviewRating.Again => 2,
        ReviewRating.Hard => 3,
        ReviewRating.Good => 4,
        _ => 5,
    };

    private static ReviewStateDto ToDto(ReviewStateDocument state) => new(
        state.QuestionId,
        state.Algorithm,
        state.Ease,
        state.IntervalDays,
        state.Reps,
        state.Lapses,
        state.DueAt,
        state.State,
        state.LastReviewedAt);
}
