using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Leaderboard;

internal static class LeaderboardEndpoints
{
    internal static void MapLeaderboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/leaderboard")
            .RequireAuthorization()
            .MapGet("", async (LeaderboardService leaderboard, CancellationToken cancellationToken) =>
                Results.Ok(await leaderboard.GetAsync(cancellationToken)));
    }
}
