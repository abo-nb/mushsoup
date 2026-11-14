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

    public int IntervalDays { get; set; }

    public int Reps { get; set; }

    public int Lapses { get; set; }

    public DateTime DueAt { get; set; }

    public string State { get; set; } = "new";

    public string? LastRating { get; set; }

    public DateTime? LastReviewedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
