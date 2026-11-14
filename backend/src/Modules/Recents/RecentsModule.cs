using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Recents;

public sealed class RecentsModule : IModule
{
    public string Name => "recents";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RecentService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapRecentEndpoints();

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RecentService>().EnsureIndexesAsync(cancellationToken);
    }
}
