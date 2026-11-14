using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Leaderboard;

public sealed class LeaderboardModule : IModule
{
    public string Name => "leaderboard";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LeaderboardService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapLeaderboardEndpoints();

    public Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
