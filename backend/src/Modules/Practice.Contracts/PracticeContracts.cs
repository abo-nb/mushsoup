using Questions.Contracts;

namespace Practice.Contracts;

public static class SessionMode
{
    public const string Sequence = "sequence";
    public const string Daily = "daily";
    public const string Endless = "endless";

    public static bool IsKnown(string mode) => mode is Sequence or Daily or Endless;
}

public sealed record SessionStatsDto(int Correct, int Wrong)
{
    public static readonly SessionStatsDto Empty = new(0, 0);
}

/// <summary>
/// Total 是本轮涉及的不同题目数；Answered 是作答次数（无限模式会超过 Total）；
/// Mastered 是本轮已练熟的题数；Remaining 是队列里还剩的题数。
/// </summary>
public sealed record SessionDto(
    string Id,
    string BankId,
    string BankName,
    string Mode,
    int Total,
    int Answered,
    int Mastered,
    int Remaining,
    DateTime StartedAt,
    DateTime? FinishedAt,
    SessionStatsDto Stats,
    string Algorithm);

/// <summary>
/// 一张待答的卡。CorrectKeys 与 Explanation 随卡下发，前端可以本地判分；
/// 这不是考试，省掉一次往返比藏答案重要。
/// </summary>
public sealed record NextCardResponse(
    SessionDto Session,
    CardDto? Card,
    int Index,
    IReadOnlyList<string> CorrectKeys,
    string Explanation);

public sealed record SubmitAnswerRequest(
    string QuestionId,
    IReadOnlyList<string> SelectedKeys,
    long ElapsedMs,
    string? Confidence);

/// <summary>State: hit | missed | wrong | neutral</summary>
public sealed record FeedbackOptionDto(string Key, string Text, string State);

public sealed record AnswerFeedbackDto(
    bool Correct,
    double Score,
    string Stem,
    IReadOnlyList<FeedbackOptionDto> Options,
    string Explanation,
    string Rating,
    int Index,
    bool Finished,
    SessionStatsDto Stats,
    SessionDto Session,
    NextCardResponse? Next);

/// <summary>Chapters 是选中的章节路径列表；每条路径都是题目 chapterPath 的前缀即命中（选大章覆盖其下小章）。</summary>
public sealed record StartSessionRequest(
    int? Limit,
    string? Mode,
    IReadOnlyList<IReadOnlyList<string>>? Chapters = null,
    string? Algorithm = null);

/// <summary>Answered 是作答次数，Correct 是其中答对的次数。</summary>
public sealed record UserPracticeStatsDto(string UserId, int Answered, int Correct);

public interface IPracticeStats
{
    /// <summary>请求里的每个 userId 都会返回一条，没有作答记录时是 0/0。</summary>
    Task<IReadOnlyList<UserPracticeStatsDto>> GetUserStatsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);
}

