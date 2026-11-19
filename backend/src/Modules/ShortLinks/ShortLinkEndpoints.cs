using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShortLinks.Contracts;

namespace ShortLinks;

internal static class ShortLinkEndpoints
{
    internal static void MapShortLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/links").RequireAuthorization();

        group.MapPost("", async (
            CreateShortLinkRequest request,
            ICurrentUser currentUser,
            ShortLinkService links,
            CancellationToken cancellationToken) =>
        {
            var link = await links.CreateAsync(
                request.Kind,
                request.TargetId,
                currentUser.RequireUserId(),
                cancellationToken);
            return Results.Ok(link);
        });

        app.MapGet("/api/public/links/{code}", async (
            string code,
            ShortLinkService links,
            CancellationToken cancellationToken) =>
        {
            var link = await links.ResolveAsync(code, cancellationToken)
                ?? throw new NotFoundException("链接不存在或已失效");
            return Results.Ok(link);
        });
    }
}
