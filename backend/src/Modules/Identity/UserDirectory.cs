using BuildingBlocks.Infrastructure;
using Identity.Contracts;
using MongoDB.Driver;

namespace Identity;

public sealed class UserDirectory(MongoDatabaseProvider provider) : IUserDirectory
{
    private IMongoCollection<UserDocument> Users => provider.Collection<UserDocument>("users");

    public async Task<UserSummaryDto?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var user = await Users
            .Find(candidate => candidate.Id == userId && candidate.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        return user is null ? null : ToSummary(user);
    }

    public async Task<IReadOnlyList<UserSummaryDto>> GetManyAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var users = await Users
            .Find(Builders<UserDocument>.Filter.In(user => user.Id, ids)
                & Builders<UserDocument>.Filter.Eq(user => user.DeletedAt, null))
            .ToListAsync(cancellationToken);

        return users.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<UserSummaryDto>> ListByClassAsync(
        string classId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(classId))
        {
            return [];
        }

        var users = await Users
            .Find(candidate => candidate.ClassId == classId && candidate.DeletedAt == null)
            .Sort(Builders<UserDocument>.Sort.Ascending(candidate => candidate.DisplayName))
            .ToListAsync(cancellationToken);

        return users.Select(ToSummary).ToList();
    }

    public Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken = default) =>
        Users.Find(candidate => candidate.Id == userId && candidate.DeletedAt == null)
            .AnyAsync(cancellationToken);

    public async Task<UserPrefsDto?> GetPrefsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await Users
            .Find(candidate => candidate.Id == userId && candidate.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        return user is null
            ? null
            : new UserPrefsDto(
                user.Prefs?.ShuffleOptions ?? UserPrefsDto.Default.ShuffleOptions,
                user.Prefs?.SubmitMode ?? UserPrefsDto.Default.SubmitMode);
    }

    private static UserSummaryDto ToSummary(UserDocument user) => new(user.Id, user.DisplayName, user.ClassId, user.AvatarUrl);
}
