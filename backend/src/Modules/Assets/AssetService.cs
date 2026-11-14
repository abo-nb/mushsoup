using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Assets.Contracts;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Assets;

internal sealed class AssetService : IAssetStore
{
    private static readonly Dictionary<string, string> ExtensionByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/webp"] = "webp",
        ["image/gif"] = "gif",
    };

    private readonly MongoDatabaseProvider _database;
    private readonly string _baseDirectory;
    private readonly long _maxBytes;

    public AssetService(MongoDatabaseProvider database, AssetsOptions options, IHostEnvironment environment)
    {
        _database = database;
        _baseDirectory = Path.GetFullPath(options.Directory, environment.ContentRootPath);
        _maxBytes = options.MaxFileBytes;
    }

    public Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_baseDirectory);
        var assets = _database.Collection<AssetDocument>("assets");
        return assets.Indexes.CreateOneAsync(
            new CreateIndexModel<AssetDocument>(Builders<AssetDocument>.IndexKeys.Ascending(x => x.Sha256)),
            cancellationToken: cancellationToken);
    }

    public async Task<AssetDto> SaveAsync(
        string ownerId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var mime = NormalizeMime(contentType);
        if (!ExtensionByMime.TryGetValue(mime, out var extension))
        {
            throw new ValidationFailedException("file", "只支持 PNG、JPEG、WebP、GIF 图片");
        }

        Directory.CreateDirectory(_baseDirectory);
        var tempDirectory = Path.Combine(_baseDirectory, "tmp");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");

        try
        {
            var (sha256, size) = await WriteTempAsync(content, tempPath, cancellationToken);
            if (size == 0)
            {
                throw new ValidationFailedException("file", "文件内容为空");
            }

            var assets = _database.Collection<AssetDocument>("assets");
            var existing = await assets.Find(x => x.Sha256 == sha256).FirstOrDefaultAsync(cancellationToken);
            string storageKey;
            if (existing is not null)
            {
                storageKey = existing.StorageKey;
            }
            else
            {
                storageKey = $"{sha256[..2]}/{sha256}.{extension}";
                var targetPath = ResolvePath(storageKey);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (!File.Exists(targetPath))
                {
                    try
                    {
                        File.Move(tempPath, targetPath);
                    }
                    catch (IOException) when (File.Exists(targetPath))
                    {
                    }
                }
            }

            var document = new AssetDocument
            {
                Id = Ids.New(),
                OwnerId = ownerId,
                FileName = SanitizeFileName(fileName, extension),
                Mime = mime,
                Size = size,
                Sha256 = sha256,
                StorageKey = storageKey,
                CreatedAt = DateTime.UtcNow,
            };
            await assets.InsertOneAsync(document, cancellationToken: cancellationToken);
            return ToDto(document.Id, document.OwnerId, document.FileName, document.Mime, document.Size);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task<(Stream Stream, string Mime, string FileName)?> OpenAsync(
        string assetId,
        CancellationToken cancellationToken = default)
    {
        var assets = _database.Collection<AssetDocument>("assets");
        var document = await assets.Find(x => x.Id == assetId).FirstOrDefaultAsync(cancellationToken);
        if (document is null)
        {
            return null;
        }

        var path = ResolvePath(document.StorageKey);
        if (!File.Exists(path))
        {
            return null;
        }

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return (stream, document.Mime, document.FileName);
    }

    public async Task<AssetDto?> GetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var assets = _database.Collection<AssetDocument>("assets");
        var document = await assets.Find(x => x.Id == assetId).FirstOrDefaultAsync(cancellationToken);
        return document is null
            ? null
            : ToDto(document.Id, document.OwnerId, document.FileName, document.Mime, document.Size);
    }

    private async Task<(string Sha256, long Size)> WriteTempAsync(
        Stream content,
        string tempPath,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var target = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);

            long size = 0;
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                size += read;
                if (size > _maxBytes)
                {
                    throw new ValidationFailedException("file", $"文件大小不能超过 {_maxBytes / 1024 / 1024} MB");
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return (Convert.ToHexStringLower(hash.GetHashAndReset()), size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private string ResolvePath(string storageKey) =>
        Path.Combine(_baseDirectory, storageKey.Replace('/', Path.DirectorySeparatorChar));

    private static string NormalizeMime(string? contentType)
    {
        var mime = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(mime) ? "application/octet-stream" : mime;
    }

    private static string SanitizeFileName(string? fileName, string extension)
    {
        var stem = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName ?? string.Empty));
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(stem.Length);
        foreach (var ch in stem)
        {
            if (char.IsControl(ch) || invalid.Contains(ch))
            {
                continue;
            }

            builder.Append(ch);
        }

        var safe = builder.ToString().Trim().Trim('.');
        if (safe.Length > 80)
        {
            safe = safe[..80].TrimEnd();
        }

        if (safe.Length == 0)
        {
            safe = "image";
        }

        return $"{safe}.{extension}";
    }

    private static AssetDto ToDto(string id, string ownerId, string fileName, string mime, long size) =>
        new(id, ownerId, fileName, mime, size, $"/api/assets/{id}/raw");
}

file sealed class AssetDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Mime { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
