using System.Security.Cryptography;
using Banks.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using MongoDB.Driver;
using Notes.Contracts;
using ShortLinks.Contracts;

namespace ShortLinks;

internal sealed class ShortLinkService(
    MongoDatabaseProvider database,
    IBankDirectory banks,
    INoteDirectory notes)
{
    private const string Alphabet = "23456789abcdefghjkmnpqrstuvwxyz";
    private const int CodeLength = 8;
    private const int InsertAttempts = 5;

    private IMongoCollection<ShortLinkDocument> Links =>
        database.Collection<ShortLinkDocument>("short_links");

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        await Links.Indexes.CreateOneAsync(
            new CreateIndexModel<ShortLinkDocument>(
                Builders<ShortLinkDocument>.IndexKeys
                    .Ascending(x => x.Kind)
                    .Ascending(x => x.TargetId),
                new CreateIndexOptions { Unique = true, Name = "kind_target" }),
            cancellationToken: cancellationToken);
    }

    /// <summary>同一个公开内容复用同一个短码，重复点分享不会生成一堆。</summary>
    public async Task<ShortLinkDto> CreateAsync(
        string kind,
        string targetId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!ShareKind.IsKnown(kind))
        {
            throw new ValidationFailedException("kind", "只支持题库和讲义");
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ValidationFailedException("targetId", "内容标识不能为空");
        }

        var trimmed = targetId.Trim();
        await EnsurePublicAsync(kind, trimmed, cancellationToken);

        var filter = Builders<ShortLinkDocument>.Filter
            .Eq(x => x.Kind, kind) & Builders<ShortLinkDocument>.Filter.Eq(x => x.TargetId, trimmed);
        var existing = await Links.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return new ShortLinkDto(existing.Id, existing.Kind, existing.TargetId);
        }

        for (var attempt = 0; attempt < InsertAttempts; attempt += 1)
        {
            var document = new ShortLinkDocument
            {
                Id = NewCode(),
                Kind = kind,
                TargetId = trimmed,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
            };

            try
            {
                await Links.InsertOneAsync(document, cancellationToken: cancellationToken);
                return new ShortLinkDto(document.Id, document.Kind, document.TargetId);
            }
            catch (MongoWriteException error) when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // 短码撞了换一个；如果是 (kind,targetId) 撞了，说明并发创建，重新读一次。
                var raced = await Links.Find(filter).FirstOrDefaultAsync(cancellationToken);
                if (raced is not null)
                {
                    return new ShortLinkDto(raced.Id, raced.Kind, raced.TargetId);
                }
            }
        }

        throw new ConflictException("短链接生成失败，请重试");
    }

    public async Task<ShortLinkDto?> ResolveAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return null;
        }

        var document = await Links.Find(x => x.Id == normalized).FirstOrDefaultAsync(cancellationToken);
        return document is null
            ? null
            : new ShortLinkDto(document.Id, document.Kind, document.TargetId);
    }

    private async Task EnsurePublicAsync(string kind, string targetId, CancellationToken cancellationToken)
    {
        if (kind == ShareKind.Bank)
        {
            var access = await banks.GetAccessAsync(targetId, "", null, cancellationToken);
            if (access is null || !access.CanView || access.Bank.Visibility != BankVisibility.Public)
            {
                throw new ValidationFailedException("targetId", "只有公开题库才能生成分享链接");
            }

            return;
        }

        if (!await notes.IsPublicAsync(targetId, cancellationToken))
        {
            throw new ValidationFailedException("targetId", "只有公开讲义才能生成分享链接");
        }
    }

    private static string NewCode()
    {
        Span<byte> buffer = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(buffer);
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i += 1)
        {
            chars[i] = Alphabet[buffer[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
