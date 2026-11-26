using MongoDB.Bson.Serialization.Attributes;

namespace Ranges;

[BsonIgnoreExtraElements]
internal sealed class RangeDocument
{
    [BsonId]
    [BsonElement("_id")]
    public string Id { get; set; } = "";

    [BsonElement("ownerId")]
    public string OwnerId { get; set; } = "";

    [BsonElement("bankId")]
    public string BankId { get; set; } = "";

    /// <summary>发布时的题库名快照，之后题库改名不影响已发布的范围。</summary>
    [BsonElement("bankName")]
    public string BankName { get; set; } = "";

    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("chapters")]
    public List<List<string>> Chapters { get; set; } = [];

    [BsonElement("questionCount")]
    public int QuestionCount { get; set; }

    [BsonElement("limit")]
    public int Limit { get; set; }

    [BsonElement("mode")]
    public string Mode { get; set; } = "";

    [BsonElement("algorithm")]
    public string? Algorithm { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }
}
