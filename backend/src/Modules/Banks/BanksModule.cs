using Banks.Contracts;
using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Banks;

public sealed class BanksModule : IModule
{
    public string Name => "banks";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<BankCache>();
        services.AddScoped<BankService>();
        services.AddScoped<IBankDirectory>(sp => sp.GetRequiredService<BankService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapBankEndpoints();

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BankService>().EnsureIndexesAsync(cancellationToken);
    }
}
