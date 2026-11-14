namespace Scheduling.Contracts;

public static class ReviewRating
{
    public const string Again = "again";
    public const string Hard = "hard";
    public const string Good = "good";
    public const string Easy = "easy";

    public static bool IsKnown(string rating) => rating is Again or Hard or Good or Easy;
}

public static class MemoryState
{
    public const string New = "new";
    public const string Learning = "learning";
    public const string Review = "review";
    public const string Mastered = "mastered";
}

public sealed record ReviewStateDto(
    string QuestionId,
    string Algorithm,
    double Ease,
    int IntervalDays,
    int Reps,
    int Lapses,
    DateTime DueAt,
    string State,
    DateTime? LastReviewedAt);

public sealed record ScheduleResult(
    ReviewStateDto State,
    string Rating,
    bool MasteredNow,
    DateTime DueAt);

/// <summary>记忆调度只认 (userId, questionId)，不认识题目内容。</summary>
public interface IReviewScheduler
{
    Task<ScheduleResult> ReviewAsync(
        string userId,
        string questionId,
        string rating,
        CancellationToken cancellationToken = default);

    Task<ReviewStateDto?> GetAsync(string userId, string questionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, ReviewStateDto>> GetManyAsync(
        string userId,
        IEnumerable<string> questionIds,
        CancellationToken cancellationToken = default);
}
