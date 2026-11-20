namespace Scheduling.Contracts;

public static class ReviewRating
{
    public const string Again = "again";
    public const string Hard = "hard";
    public const string Good = "good";
    public const string Easy = "easy";

    public static bool IsKnown(string rating) => rating is Again or Hard or Good or Easy;
}

/// <summary>sm2：间隔复习；speed：速度优先；fsrs：FSRS-5；leitner：固定档位的盒子。</summary>
public static class ReviewAlgorithm
{
    public const string Sm2 = "sm2";
    public const string Speed = "speed";
    public const string Fsrs = "fsrs";
    public const string Leitner = "leitner";

    public static bool IsKnown(string algorithm) => algorithm is Sm2 or Speed or Fsrs or Leitner;

    /// <summary>需要按到期时间排队、并且有「间隔」概念的算法。</summary>
    public static bool IsScheduled(string algorithm) => algorithm is Sm2 or Fsrs or Leitner;
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
        string algorithm,
        CancellationToken cancellationToken = default);

    Task<ReviewStateDto?> GetAsync(
        string userId,
        string questionId,
        string algorithm,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, ReviewStateDto>> GetManyAsync(
        string userId,
        IEnumerable<string> questionIds,
        string algorithm,
        CancellationToken cancellationToken = default);
}
