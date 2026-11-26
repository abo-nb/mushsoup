using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ranges;

public sealed class RangesModule : IModule
{
    public string Name => "ranges";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<RangeCache>();
        services.AddScoped<RangeService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapRangeEndpoints();

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RangeService>().EnsureIndexesAsync(cancellationToken);
    }
}
