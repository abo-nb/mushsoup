using MongoDB.Bson.Serialization.Attributes;
using Notes.Contracts;

namespace Notes;

[BsonIgnoreExtraElements]
internal sealed class NoteDocument
{
    [BsonId]
    [BsonElement("_id")]
    public string Id { get; set; } = "";

    [BsonElement("ownerId")]
    public string OwnerId { get; set; } = "";

    [BsonElement("classId")]
    public string ClassId { get; set; } = "";

    [BsonElement("title")]
    public string Title { get; set; } = "";

    [BsonElement("subtitle")]
    public string? Subtitle { get; set; }

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = [];

    [BsonElement("visibility")]
    public string Visibility { get; set; } = NoteVisibility.Public;

    [BsonElement("year")]
    public string Year { get; set; } = "";

    [BsonElement("semester")]
    public string Semester { get; set; } = "";

    [BsonElement("sectionCount")]
    public int SectionCount { get; set; }

    /// <summary>整份讲义 JSON 原文，读取时解析成 JsonElement 返回。</summary>
    [BsonElement("contentJson")]
    public string ContentJson { get; set; } = "";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }
}
