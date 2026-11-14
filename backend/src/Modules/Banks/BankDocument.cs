using Banks.Contracts;
using MongoDB.Bson.Serialization.Attributes;

namespace Banks;

[BsonIgnoreExtraElements]
internal sealed class BankDocument
{
    [BsonId]
    [BsonElement("_id")]
    public string Id { get; set; } = "";

    [BsonElement("classId")]
    public string ClassId { get; set; } = "";

    [BsonElement("ownerId")]
    public string OwnerId { get; set; } = "";

    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = [];

    [BsonElement("year")]
    public string Year { get; set; } = "";

    [BsonElement("semester")]
    public string Semester { get; set; } = "";

    [BsonElement("visibility")]
    public string Visibility { get; set; } = BankVisibility.Private;

    [BsonElement("settings")]
    public BankSettings Settings { get; set; } = new();

    [BsonElement("memberIds")]
    public List<string> MemberIds { get; set; } = [];

    [BsonElement("questionCount")]
    public int QuestionCount { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }
}

internal sealed class BankSettings
{
    [BsonElement("defaultShuffleOptions")]
    public bool DefaultShuffleOptions { get; set; } = true;

    [BsonElement("requireReview")]
    public bool RequireReview { get; set; }
}
