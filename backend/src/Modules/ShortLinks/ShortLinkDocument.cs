using MongoDB.Bson.Serialization.Attributes;

namespace ShortLinks;

internal sealed class ShortLinkDocument
{
    /// <summary>短码本身就是主键。</summary>
    [BsonId]
    [BsonElement("_id")]
    public string Id { get; set; } = "";

    [BsonElement("kind")]
    public string Kind { get; set; } = "";

    [BsonElement("targetId")]
    public string TargetId { get; set; } = "";

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = "";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}
