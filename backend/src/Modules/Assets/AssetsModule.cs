using Assets.Contracts;
using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Assets;

public sealed class AssetsModule : IModule
{
    public string Name => "assets";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Assets");
        services.AddSingleton(new AssetsOptions
        {
            Directory = section["Directory"] ?? "data/assets",
            MaxFileBytes = section.GetValue<long?>("MaxFileBytes") ?? 5 * 1024 * 1024,
        });
        services.AddSingleton<AssetService>();
        services.AddSingleton<IAssetStore>(provider => provider.GetRequiredService<AssetService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapAssetEndpoints();

    public Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<AssetService>().EnsureIndexesAsync(cancellationToken);
}
