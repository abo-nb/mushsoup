using BuildingBlocks.Contracts;
using Favorites.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Favorites;

internal static class FavoriteEndpoints
{
    internal static void MapFavoriteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/favorites").RequireAuthorization();

        group.MapGet("", async (
            ICurrentUser currentUser,
            FavoriteService favorites,
            CancellationToken cancellationToken) =>
        {
            var items = await favorites.ListAsync(currentUser.RequireUserId(), cancellationToken);
            return Results.Ok(items);
        });

        group.MapPost("", async (
            AddFavoriteRequest request,
            ICurrentUser currentUser,
            FavoriteService favorites,
            CancellationToken cancellationToken) =>
        {
            await favorites.AddAsync(
                currentUser.RequireUserId(),
                currentUser.ClassId,
                request.Kind,
                request.ItemId,
                cancellationToken);
            return Results.NoContent();
        });

        group.MapDelete("/{kind}/{itemId}", async (
            string kind,
            string itemId,
            ICurrentUser currentUser,
            FavoriteService favorites,
            CancellationToken cancellationToken) =>
        {
            await favorites.RemoveAsync(currentUser.RequireUserId(), kind, itemId, cancellationToken);
            return Results.NoContent();
        });
    }
}
