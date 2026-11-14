using MongoDB.Bson.Serialization.Attributes;

namespace Recents;

static class RecentCollections
{
    public const string Items = "recent_items";
}

[BsonIgnoreExtraElements]
internal sealed class RecentDocument
{
    /// <summary>由 userId、kind、itemId 拼成，天然唯一，省掉一条唯一索引。</summary>
    [BsonId]
    [BsonElement("_id")]
    public string Id { get; set; } = "";

    [BsonElement("userId")]
    public string UserId { get; set; } = "";

    [BsonElement("kind")]
    public string Kind { get; set; } = "";

    [BsonElement("itemId")]
    public string ItemId { get; set; } = "";

    [BsonElement("lastOpenedAt")]
    public DateTime LastOpenedAt { get; set; }

    [BsonElement("openCount")]
    public int OpenCount { get; set; }
}
