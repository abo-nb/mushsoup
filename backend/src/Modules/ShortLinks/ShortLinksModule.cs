using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShortLinks;

public sealed class ShortLinksModule : IModule
{
    public string Name => "short-links";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ShortLinkService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapShortLinkEndpoints();

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ShortLinkService>().EnsureIndexesAsync(cancellationToken);
    }
}
