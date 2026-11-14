using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using MongoDB.Driver;
using Recents.Contracts;

namespace Recents;

internal sealed class RecentService(MongoDatabaseProvider database)
{
    private const int DefaultLimit = 6;
    private const int MaxLimit = 20;

    private IMongoCollection<RecentDocument> Items =>
        database.Collection<RecentDocument>(RecentCollections.Items);

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        await Items.Indexes.CreateOneAsync(
            new CreateIndexModel<RecentDocument>(
                Builders<RecentDocument>.IndexKeys
                    .Ascending(x => x.UserId)
                    .Descending(x => x.LastOpenedAt),
                new CreateIndexOptions { Name = "userId_lastOpenedAt" }),
            cancellationToken: cancellationToken);
    }

    public async Task RecordAsync(string userId, string kind, string itemId, CancellationToken cancellationToken)
    {
        if (kind is not (RecentKind.Bank or RecentKind.Note))
        {
            throw new ValidationFailedException("kind", "只记录题库和讲义");
        }

        if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 64)
        {
            throw new ValidationFailedException("itemId", "内容标识不合法");
        }

        var update = Builders<RecentDocument>.Update
            .Set(x => x.UserId, userId)
            .Set(x => x.Kind, kind)
            .Set(x => x.ItemId, itemId)
            .Set(x => x.LastOpenedAt, DateTime.UtcNow)
            .Inc(x => x.OpenCount, 1);

        await Items.UpdateOneAsync(
            Builders<RecentDocument>.Filter.Eq(x => x.Id, Key(userId, kind, itemId)),
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<IReadOnlyList<RecentItemDto>> ListAsync(
        string userId,
        int? limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var items = await Items
            .Find(Builders<RecentDocument>.Filter.Eq(x => x.UserId, userId))
            .Sort(Builders<RecentDocument>.Sort
                .Descending(x => x.LastOpenedAt)
                .Descending(x => x.OpenCount))
            .Limit(take)
            .ToListAsync(cancellationToken);

        return items
            .Select(x => new RecentItemDto(x.Kind, x.ItemId, x.LastOpenedAt, x.OpenCount))
            .ToList();
    }

    private static string Key(string userId, string kind, string itemId) => $"{userId}|{kind}|{itemId}";
}
