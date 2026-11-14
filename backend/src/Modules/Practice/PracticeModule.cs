using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Practice.Contracts;

namespace Practice;

public sealed class PracticeModule : IModule
{
    public string Name => "practice";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PracticeService>();
        services.AddScoped<IPracticeStats>(serviceProvider => serviceProvider.GetRequiredService<PracticeService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapPracticeEndpoints();

    public Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        PracticeService.EnsureIndexesAsync(services.GetRequiredService<MongoDatabaseProvider>(), cancellationToken);
}
