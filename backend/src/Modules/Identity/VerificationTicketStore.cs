using BuildingBlocks.Infrastructure;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Identity;

internal sealed class VerificationTicketDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    [BsonElement("requestId")]
    public string RequestId { get; set; } = "";

    [BsonElement("discordUserId")]
    public string DiscordUserId { get; set; } = "";

    [BsonElement("delivery")]
    public string Delivery { get; set; } = "dm";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// 一次性验证票据：前端只拿得到本站的 ticketId，上游 requestId 留在服务端。
/// </summary>
public sealed class VerificationTicketStore(MongoDatabaseProvider provider)
{
    private IMongoCollection<VerificationTicketDocument> Tickets =>
        provider.Collection<VerificationTicketDocument>("verification_tickets");

    public static async Task EnsureIndexesAsync(
        MongoDatabaseProvider provider,
        CancellationToken cancellationToken)
    {
        var tickets = provider.Collection<VerificationTicketDocument>("verification_tickets");
        await tickets.Indexes.CreateOneAsync(
            new CreateIndexModel<VerificationTicketDocument>(
                Builders<VerificationTicketDocument>.IndexKeys.Ascending(ticket => ticket.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            cancellationToken: cancellationToken);
    }

    internal Task CreateAsync(VerificationTicketDocument ticket, CancellationToken cancellationToken) =>
        Tickets.InsertOneAsync(ticket, cancellationToken: cancellationToken);

    internal async Task<VerificationTicketDocument?> GetAsync(string ticketId, CancellationToken cancellationToken) =>
        await Tickets.Find(ticket => ticket.Id == ticketId).FirstOrDefaultAsync(cancellationToken);

    internal Task DeleteAsync(string ticketId, CancellationToken cancellationToken) =>
        Tickets.DeleteOneAsync(ticket => ticket.Id == ticketId, cancellationToken);
}
