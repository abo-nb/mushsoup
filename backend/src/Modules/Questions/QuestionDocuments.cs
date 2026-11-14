using MongoDB.Bson.Serialization.Attributes;
using Questions.Contracts;

namespace Questions;

internal static class QuestionCollections
{
    public const string Questions = "questions";

    public const string Revisions = "question_revisions";
}

internal sealed class QuestionOptionDocument
{
    [BsonElement("key")]
    public string Key { get; set; } = "";

    [BsonElement("text")]
    public string Text { get; set; } = "";

    [BsonElement("correct")]
    public bool Correct { get; set; }
}

internal sealed class DistractorRuleDocument
{
    [BsonElement("poolId")]
    public string PoolId { get; set; } = "";

    [BsonElement("count")]
    public int Count { get; set; }
}

internal sealed class QuestionDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    [BsonElement("bankId")]
    public string BankId { get; set; } = "";

    [BsonElement("type")]
    public string Type { get; set; } = "";

    [BsonElement("stem")]
    public string Stem { get; set; } = "";

    [BsonElement("options")]
    public List<QuestionOptionDocument> Options { get; set; } = [];

    [BsonElement("shuffle")]
    public string Shuffle { get; set; } = ShuffleMode.Follow;

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = [];

    [BsonElement("chapterPath")]
    public List<string> ChapterPath { get; set; } = [];

    [BsonElement("difficulty")]
    public int Difficulty { get; set; } = 3;

    [BsonElement("source")]
    public string? Source { get; set; }

    [BsonElement("explanation")]
    public string Explanation { get; set; } = "";

    [BsonElement("status")]
    public string Status { get; set; } = QuestionStatus.Published;

    [BsonElement("unreviewed")]
    public bool Unreviewed { get; set; }

    [BsonElement("version")]
    public int Version { get; set; }

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = "";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("contentHash")]
    public string ContentHash { get; set; } = "";

    [BsonElement("distractorRule")]
    public DistractorRuleDocument? DistractorRule { get; set; }

    [BsonElement("importBatchId")]
    public string? ImportBatchId { get; set; }

    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }
}

internal sealed class QuestionRevisionDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    [BsonElement("questionId")]
    public string QuestionId { get; set; } = "";

    [BsonElement("version")]
    public int Version { get; set; }

    [BsonElement("type")]
    public string Type { get; set; } = "";

    [BsonElement("stem")]
    public string Stem { get; set; } = "";

    [BsonElement("optionsJson")]
    public string OptionsJson { get; set; } = "";

    [BsonElement("answerKeysJson")]
    public string AnswerKeysJson { get; set; } = "";

    [BsonElement("editedBy")]
    public string EditedBy { get; set; } = "";

    [BsonElement("editedAt")]
    public DateTime EditedAt { get; set; }
}

internal static class QuestionMapping
{
    public static QuestionDto ToDto(QuestionDocument document) => new(
        document.Id,
        document.BankId,
        document.Type,
        document.Stem,
        document.Options.Select(o => new QuestionOptionDto(o.Key, o.Text, o.Correct)).ToList(),
        document.Shuffle,
        document.Tags.ToList(),
        document.Difficulty,
        document.Source,
        document.Explanation,
        document.Status,
        document.Unreviewed,
        document.Version,
        document.CreatedBy,
        document.CreatedAt,
        document.UpdatedAt,
        document.ChapterPath.ToList(),
        document.DistractorRule is null
            ? null
            : new DistractorRuleDto(document.DistractorRule.PoolId, document.DistractorRule.Count));

    public static QuestionSummaryDto ToSummary(QuestionDocument document) => new(
        document.Id,
        document.Stem,
        document.Type,
        document.Tags.ToList(),
        document.Difficulty,
        document.Options.Count,
        document.Status,
        document.UpdatedAt,
        document.ChapterPath.ToList(),
        document.DistractorRule is not null);

    public static QuestionRevisionDto ToRevisionDto(QuestionRevisionDocument document) => new(
        document.Version,
        document.Type,
        document.Stem,
        document.EditedBy,
        document.EditedAt);
}
