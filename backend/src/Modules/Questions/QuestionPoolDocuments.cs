using MongoDB.Bson.Serialization.Attributes;

namespace Questions;

internal static class QuestionPoolCollections
{
    public const string Pools = "option_pools";

    public const string Entries = "option_pool_entries";
}

internal sealed class OptionPoolDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    [BsonElement("bankId")]
    public string BankId { get; set; } = "";

    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }
}

internal sealed class OptionPoolEntryDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    [BsonElement("poolId")]
    public string PoolId { get; set; } = "";

    [BsonElement("bankId")]
    public string BankId { get; set; } = "";

    [BsonElement("text")]
    public string Text { get; set; } = "";

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = [];

    [BsonElement("disabled")]
    public bool Disabled { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}
