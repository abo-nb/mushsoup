using MongoDB.Bson.Serialization.Attributes;

namespace Practice;

static class PracticeCollections
{
    public const string Sessions = "practice_sessions";
    public const string ReviewEvents = "review_events";
}

sealed class PracticeSessionDocument
{
    [BsonId] public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    public string BankId { get; set; } = "";

    public string BankName { get; set; } = "";

    public string Mode { get; set; } = "sequence";

    /// <summary>sm2 或 speed；老会话没有这个字段时按 sm2 处理。</summary>
    public string Algorithm { get; set; } = "sm2";

    /// <summary>本轮涉及的不同题目，用于计算总量。</summary>
    public List<string> QuestionIds { get; set; } = [];

    /// <summary>待做的题目队列。无限模式会把答错的题重新插回来。</summary>
    public List<string> Queue { get; set; } = [];

    public List<string> Mastered { get; set; } = [];

    /// <summary>无限模式下每道题的连续答对次数。</summary>
    public Dictionary<string, int> Streaks { get; set; } = [];

    /// <summary>已经发出去、还没作答的题。</summary>
    public string? CurrentQuestionId { get; set; }

    /// <summary>发出去的那张卡的原样快照，保证判分反馈和用户看到的选项一致。</summary>
    public string? CurrentCardJson { get; set; }

    /// <summary>按顺序记录发过的卡，用于「第几次作答」。</summary>
    public List<string> Served { get; set; } = [];

    public int Correct { get; set; }

    public int Wrong { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}

sealed class ReviewEventDocument
{
    [BsonId] public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    public string QuestionId { get; set; } = "";

    public string SessionId { get; set; } = "";

    public List<string> SelectedKeys { get; set; } = [];

    public bool Correct { get; set; }

    public double Score { get; set; }

    public string Rating { get; set; } = "";

    public long ElapsedMs { get; set; }

    public string Seed { get; set; } = "";

    public string CardJson { get; set; } = "";

    public DateTime OccurredAt { get; set; }
}
