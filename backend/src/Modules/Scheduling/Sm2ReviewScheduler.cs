using BuildingBlocks.Infrastructure;
using MongoDB.Driver;
using Scheduling.Contracts;

namespace Scheduling;

/// <summary>
/// SM-2。选它而不是 FSRS 是因为它的常数是明确定义的、可以逐条验证；
/// FSRS 的默认权重随版本变化，与其猜一组数字，不如先把接口和状态模型立住。
/// </summary>
public sealed class Sm2ReviewScheduler(MongoDatabaseProvider database, SchedulingOptions options) : IReviewScheduler
{
    private IMongoCollection<ReviewStateDocument> States =>
        database.Collection<ReviewStateDocument>(SchedulingCollections.ReviewStates);

    public async Task<ScheduleResult> ReviewAsync(
        string userId,
        string questionId,
        string rating,
        CancellationToken cancellationToken = default)
    {
        var effectiveRating = ReviewRating.IsKnown(rating) ? rating : ReviewRating.Good;
        var now = DateTime.UtcNow;

        var state = await States
            .Find(x => x.UserId == userId && x.QuestionId == questionId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? new ReviewStateDocument
            {
                Id = $"{userId}:{questionId}",
                UserId = userId,
                QuestionId = questionId,
                Ease = options.InitialEase,
                State = MemoryState.New,
            };

        var wasMastered = state.State == MemoryState.Mastered;
        Apply(state, effectiveRating, now);
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

    public async Task<ReviewStateDto?> GetAsync(string userId, string questionId, CancellationToken cancellationToken = default)
    {
        var state = await States
            .Find(x => x.UserId == userId && x.QuestionId == questionId)
            .FirstOrDefaultAsync(cancellationToken);
        return state is null ? null : ToDto(state);
    }

    public async Task<IReadOnlyDictionary<string, ReviewStateDto>> GetManyAsync(
        string userId,
        IEnumerable<string> questionIds,
        CancellationToken cancellationToken = default)
    {
        var ids = questionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, ReviewStateDto>();
        }

        var states = await States
            .Find(x => x.UserId == userId && ids.Contains(x.QuestionId))
            .ToListAsync(cancellationToken);

        return states.ToDictionary(x => x.QuestionId, ToDto);
    }

    private void Apply(ReviewStateDocument state, string rating, DateTime now)
    {
        var quality = rating switch
        {
            ReviewRating.Again => 2,
            ReviewRating.Hard => 3,
            ReviewRating.Good => 4,
            _ => 5,
        };

        state.Ease = Math.Max(1.3, state.Ease + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02)));

        if (quality < 3)
        {
            state.Reps = 0;
            state.Lapses += 1;
            state.IntervalDays = 0;
            state.DueAt = now.AddMinutes(options.RelearnMinutes);
            state.State = MemoryState.Learning;
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
