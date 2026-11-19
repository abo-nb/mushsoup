using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Favorites;

public sealed class FavoritesModule : IModule
{
    public string Name => "favorites";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FavoriteService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapFavoriteEndpoints();

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FavoriteService>().EnsureIndexesAsync(cancellationToken);
    }
}
