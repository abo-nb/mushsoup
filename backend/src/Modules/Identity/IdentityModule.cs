using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Identity;

public sealed class IdentityModule : IModule
{
    private const string DefaultVerificationBaseUrl = "https://ialine2dc.onrender.com";

    public string Name => "identity";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(VerificationOptions.SectionName);
        var verification = section.Get<VerificationOptions>() ?? new VerificationOptions();

        services.Configure<VerificationOptions>(section);
        services.AddMemoryCache();
        services.AddHttpClient<DiscordVerificationClient>(client =>
        {
            client.BaseAddress = new Uri(NormalizeBaseUrl(verification.BaseUrl));
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<VerificationTicketStore>();
        services.AddScoped<UserService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapMeEndpoints();
    }

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var provider = services.GetRequiredService<MongoDatabaseProvider>();
        await UserService.EnsureIndexesAsync(provider, cancellationToken);
        await VerificationTicketStore.EnsureIndexesAsync(provider, cancellationToken);
    }

    private static string NormalizeBaseUrl(string? baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl)
            ? DefaultVerificationBaseUrl + "/"
            : baseUrl.TrimEnd('/') + "/";
}
