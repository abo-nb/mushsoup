using System.Security.Cryptography;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Classes.Contracts;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Classes;

internal sealed class ClassDocument
{
    [BsonId]
    public string Id { get; set; } = Ids.New();

    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("inviteCode")]
    public string InviteCode { get; set; } = "";

    [BsonElement("adminIds")]
    public List<string> AdminIds { get; set; } = [];

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ClassService(MongoDatabaseProvider provider) : IClassDirectory
{
    private const string DefaultClassId = "default";
    private const string DefaultClassName = "我的班级";
    private const string DefaultAdminUserId = "568768836153769996";
    private const string InviteCodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private IMongoCollection<ClassDocument> Classes => provider.Collection<ClassDocument>("classes");

    public static async Task EnsureIndexesAsync(MongoDatabaseProvider provider, CancellationToken cancellationToken)
    {
        var classes = provider.Collection<ClassDocument>("classes");
        await classes.Indexes.CreateOneAsync(
            new CreateIndexModel<ClassDocument>(
                Builders<ClassDocument>.IndexKeys.Ascending(item => item.InviteCode),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

        // 幂等回填：只处理默认班，且仅在其没有任何管理员时补上内置管理员，不覆盖已有管理员。
        await classes.UpdateOneAsync(
            Builders<ClassDocument>.Filter.Eq(item => item.Id, DefaultClassId)
                & Builders<ClassDocument>.Filter.Or(
                    Builders<ClassDocument>.Filter.Size(item => item.AdminIds, 0),
                    Builders<ClassDocument>.Filter.Eq(item => item.AdminIds, null)),
            Builders<ClassDocument>.Update.AddToSet(item => item.AdminIds, DefaultAdminUserId),
            cancellationToken: cancellationToken);

        if (await classes.CountDocumentsAsync(FilterDefinition<ClassDocument>.Empty, cancellationToken: cancellationToken) > 0)
        {
            return;
        }

        try
        {
            // 固定默认班级的 Id，保证多个启动实例并发种子时不会建出两个默认班。
            await classes.InsertOneAsync(
                new ClassDocument
                {
                    Id = DefaultClassId,
                    Name = DefaultClassName,
                    InviteCode = NewInviteCode(),
                    AdminIds = [DefaultAdminUserId],
                },
                cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
        }
    }

    public async Task<ClassDto?> GetAsync(string classId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(classId))
        {
            return null;
        }

        var document = await Classes
            .Find(candidate => candidate.Id == classId)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : ToDto(document);
    }

    public async Task<ClassDto?> FindByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            return null;
        }

        var code = inviteCode.Trim().ToUpperInvariant();
        var document = await Classes
            .Find(candidate => candidate.InviteCode == code)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : ToDto(document);
    }

    public async Task<ClassDto> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var document = await Classes
            .Find(FilterDefinition<ClassDocument>.Empty)
            .Sort(Builders<ClassDocument>.Sort.Ascending(item => item.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? ToDto(await CreateDefaultAsync(cancellationToken)) : ToDto(document);
    }

    public Task<bool> IsAdminAsync(string classId, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(classId) || string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(false);
        }

        return Classes
            .Find(Builders<ClassDocument>.Filter.Eq(item => item.Id, classId)
                & Builders<ClassDocument>.Filter.AnyEq(item => item.AdminIds, userId))
            .AnyAsync(cancellationToken);
    }

    private async Task<ClassDocument> CreateDefaultAsync(CancellationToken cancellationToken)
    {
        var document = new ClassDocument
        {
            Id = DefaultClassId,
            Name = DefaultClassName,
            InviteCode = NewInviteCode(),
            AdminIds = [DefaultAdminUserId],
        };

        try
        {
            await Classes.InsertOneAsync(document, cancellationToken: cancellationToken);
            return document;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await Classes
                .Find(candidate => candidate.Id == DefaultClassId)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private static string NewInviteCode() => RandomNumberGenerator.GetString(InviteCodeAlphabet, 6);

    private static ClassDto ToDto(ClassDocument document) =>
        new(document.Id, document.Name, document.InviteCode, document.AdminIds);
}
