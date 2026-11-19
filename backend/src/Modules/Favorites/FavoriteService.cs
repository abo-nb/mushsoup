using Banks.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Favorites.Contracts;
using MongoDB.Driver;
using Notes.Contracts;

namespace Favorites;

internal sealed class FavoriteService(
    MongoDatabaseProvider database,
    IBankDirectory banks,
    INoteDirectory notes)
{
    private const int MaxItemIdLength = 64;

    private IMongoCollection<FavoriteDocument> Favorites =>
        database.Collection<FavoriteDocument>("favorite_items");

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        await Favorites.Indexes.CreateOneAsync(
            new CreateIndexModel<FavoriteDocument>(
                Builders<FavoriteDocument>.IndexKeys
                    .Ascending(x => x.UserId)
                    .Descending(x => x.CreatedAt),
                new CreateIndexOptions { Name = "userId_createdAt" }),
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<FavoriteItemDto>> ListAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var items = await Favorites
            .Find(Builders<FavoriteDocument>.Filter.Eq(x => x.UserId, userId))
            .Sort(Builders<FavoriteDocument>.Sort.Descending(x => x.CreatedAt))
            .ToListAsync(cancellationToken);

        return items
            .Select(x => new FavoriteItemDto(x.Kind, x.ItemId, x.CreatedAt))
            .ToList();
    }

    /// <summary>只允许收藏当前公开的题库和讲义；重复收藏是幂等的。</summary>
    public async Task AddAsync(
        string userId,
        string? classId,
        string kind,
        string? itemId,
        CancellationToken cancellationToken)
    {
        if (!FavoriteKind.IsKnown(kind))
        {
            throw new ValidationFailedException("kind", "只支持题库和讲义");
        }

        var targetId = itemId?.Trim() ?? "";
        if (targetId.Length == 0 || targetId.Length > MaxItemIdLength)
        {
            throw new ValidationFailedException("itemId", "内容标识不合法");
        }

        await EnsurePublicAsync(userId, classId, kind, targetId, cancellationToken);

        var update = Builders<FavoriteDocument>.Update
            .SetOnInsert(x => x.UserId, userId)
            .SetOnInsert(x => x.Kind, kind)
            .SetOnInsert(x => x.ItemId, targetId)
            .SetOnInsert(x => x.CreatedAt, DateTime.UtcNow);

        await Favorites.UpdateOneAsync(
            Builders<FavoriteDocument>.Filter.Eq(x => x.Id, Key(userId, kind, targetId)),
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task RemoveAsync(
        string userId,
        string kind,
        string itemId,
        CancellationToken cancellationToken)
    {
        if (!FavoriteKind.IsKnown(kind))
        {
            throw new ValidationFailedException("kind", "只支持题库和讲义");
        }

        await Favorites.DeleteOneAsync(
            Builders<FavoriteDocument>.Filter.Eq(x => x.Id, Key(userId, kind, itemId.Trim())),
            cancellationToken);
    }

    private async Task EnsurePublicAsync(
        string userId,
        string? classId,
        string kind,
        string itemId,
        CancellationToken cancellationToken)
    {
        if (kind == FavoriteKind.Bank)
        {
            var access = await banks.GetAccessAsync(itemId, userId, classId, cancellationToken);
            if (access is null || !access.CanView || access.Bank.Visibility != BankVisibility.Public)
            {
                throw new NotFoundException("题库不存在或未公开");
            }

            return;
        }

        if (!await notes.IsPublicAsync(itemId, cancellationToken))
        {
            throw new NotFoundException("讲义不存在或未公开");
        }
    }

    private static string Key(string userId, string kind, string itemId) => $"{userId}|{kind}|{itemId}";
}
