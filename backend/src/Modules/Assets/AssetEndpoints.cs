using Assets.Contracts;
using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Assets;

internal static class AssetEndpoints
{
    internal static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assets").RequireAuthorization();

        group.MapPost("", UploadAsync).DisableAntiforgery();
        group.MapGet("/{id}/raw", GetRawAsync);
    }

    private static async Task<IResult> UploadAsync(
        IFormFile? file,
        ICurrentUser currentUser,
        IAssetStore store,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new ValidationFailedException("file", "请选择要上传的图片");
        }

        await using var stream = file.OpenReadStream();
        var asset = await store.SaveAsync(
            currentUser.RequireUserId(),
            file.FileName,
            file.ContentType,
            stream,
            cancellationToken);
        return Results.Created($"/api/assets/{asset.Id}/raw", asset);
    }

    private static async Task<IResult> GetRawAsync(
        HttpContext context,
        string id,
        IAssetStore store,
        CancellationToken cancellationToken)
    {
        var opened = await store.OpenAsync(id, cancellationToken);
        if (opened is null)
        {
            throw new NotFoundException("资产不存在");
        }

        var (stream, mime, _) = opened.Value;
        context.Response.Headers.CacheControl = "private, max-age=31536000";
        return Results.File(stream, mime, enableRangeProcessing: true);
    }
}
