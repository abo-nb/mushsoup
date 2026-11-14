namespace Assets.Contracts;

public sealed record AssetDto(string Id, string OwnerId, string FileName, string Mime, long Size, string Url);

public interface IAssetStore
{
    Task<AssetDto> SaveAsync(
        string ownerId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<(Stream Stream, string Mime, string FileName)?> OpenAsync(string assetId, CancellationToken cancellationToken = default);

    Task<AssetDto?> GetAsync(string assetId, CancellationToken cancellationToken = default);
}
