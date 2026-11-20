using MongoDB.Bson.Serialization.Attributes;

namespace Scheduling;

static class SchedulingCollections
{
    public const string ReviewStates = "review_states";
}

sealed class ReviewStateDocument
{
    [BsonId] public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    public string QuestionId { get; set; } = "";

    public string Algorithm { get; set; } = "sm2";

    public double Ease { get; set; }

    /// <summary>FSRS 的稳定度（天）。</summary>
    public double Stability { get; set; }

    /// <summary>FSRS 的难度（1..10）。</summary>
    public double Difficulty { get; set; }

    /// <summary>Leitner 当前所在格（0..6）。</summary>
    public int Box { get; set; }

    /// <summary>SM-2 当前走到第几个学习步。</summary>
    public int LearningStep { get; set; }

    /// <summary>SM-2 上次按天计的间隔，答错后用它折半恢复。</summary>
    public double PreviousIntervalDays { get; set; }

    public int IntervalDays { get; set; }

    public int Reps { get; set; }

    public int Lapses { get; set; }

    public DateTime DueAt { get; set; }

    public string State { get; set; } = "new";

    public string? LastRating { get; set; }

    public DateTime? LastReviewedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
