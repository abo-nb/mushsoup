using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Recents.Contracts;

namespace Recents;

internal static class RecentEndpoints
{
    internal static void MapRecentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recents").RequireAuthorization();

        group.MapGet("", async (
            int? limit,
            ICurrentUser currentUser,
            RecentService recents,
            CancellationToken cancellationToken) =>
        {
            var items = await recents.ListAsync(currentUser.RequireUserId(), limit, cancellationToken);
            return Results.Ok(items);
        });

        group.MapPost("", async (
            RecordRecentRequest request,
            ICurrentUser currentUser,
            RecentService recents,
            CancellationToken cancellationToken) =>
        {
            await recents.RecordAsync(
                currentUser.RequireUserId(),
                request.Kind,
                request.ItemId,
                cancellationToken);
            return Results.NoContent();
        });
    }
}
