using MongoDB.Bson.Serialization.Attributes;

namespace Favorites;

[BsonIgnoreExtraElements]
internal sealed class FavoriteDocument
{
    /// <summary>由 userId、kind、itemId 拼成，天然唯一。</summary>
    [BsonId]
    [BsonElement("_id")]
    public string Id { get; set; } = "";

    [BsonElement("userId")]
    public string UserId { get; set; } = "";

    [BsonElement("kind")]
    public string Kind { get; set; } = "";

    [BsonElement("itemId")]
    public string ItemId { get; set; } = "";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}
