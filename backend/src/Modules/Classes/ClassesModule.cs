using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Classes.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Classes;

public sealed class ClassesModule : IModule
{
    public string Name => "classes";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ClassService>();
        services.AddScoped<IClassDirectory>(serviceProvider => serviceProvider.GetRequiredService<ClassService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapClassesEndpoints();

    public Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        ClassService.EnsureIndexesAsync(
            services.GetRequiredService<MongoDatabaseProvider>(),
            cancellationToken);
}
