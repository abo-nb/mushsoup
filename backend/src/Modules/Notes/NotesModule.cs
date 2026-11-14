using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notes.Contracts;

namespace Notes;

public sealed class NotesModule : IModule
{
    public string Name => "notes";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<NoteCache>();
        services.AddScoped<NoteService>();
        services.AddScoped<INoteDirectory>(serviceProvider => serviceProvider.GetRequiredService<NoteService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapNoteEndpoints();

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NoteService>().EnsureIndexesAsync(cancellationToken);
    }
}
