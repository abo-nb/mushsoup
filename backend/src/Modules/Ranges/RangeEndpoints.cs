using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ranges.Contracts;

namespace Ranges;

internal static class RangeEndpoints
{
    internal static void MapRangeEndpoints(this IEndpointRouteBuilder app)
    {
        // 公开入口：游客也能看范围和打开分享链接。
        var group = app.MapGroup("/api/ranges");

        group.MapGet("", async (
            ICurrentUser currentUser,
            RangeService ranges,
            CancellationToken cancellationToken) =>
        {
            var items = await ranges.ListPublicAsync(currentUser.UserId, cancellationToken);
            return Results.Ok(items);
        });

        group.MapGet("/{id}", async (
            string id,
            ICurrentUser currentUser,
            RangeService ranges,
            CancellationToken cancellationToken) =>
        {
            var range = await ranges.GetVisibleAsync(
                id,
                currentUser.UserId,
                currentUser.ClassId,
                cancellationToken)
                ?? throw new NotFoundException("范围不存在");
            return Results.Ok(range);
        });

        group.MapPost("", async (
            CreateRangeRequest request,
            ICurrentUser currentUser,
            RangeService ranges,
            CancellationToken cancellationToken) =>
        {
            var range = await ranges.CreateAsync(
                request,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken);
            return Results.Created($"/api/ranges/{range.Id}", range);
        }).RequireAuthorization();

        group.MapDelete("/{id}", async (
            string id,
            ICurrentUser currentUser,
            RangeService ranges,
            CancellationToken cancellationToken) =>
        {
            await ranges.DeleteAsync(id, currentUser.RequireUserId(), cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
