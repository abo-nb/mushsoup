using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Scheduling.Contracts;

namespace Scheduling;

public sealed class SchedulingModule : IModule
{
    public string Name => "scheduling";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Scheduling").Get<SchedulingOptions>() ?? new SchedulingOptions();
        services.AddSingleton(options);
        services.AddScoped<IReviewScheduler, Sm2ReviewScheduler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // 调度只被 Practice 通过接口调用，P1 不对外暴露端点。
    }

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var database = services.GetRequiredService<MongoDatabaseProvider>();
        var states = database.Collection<ReviewStateDocument>(SchedulingCollections.ReviewStates);
        await states.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ReviewStateDocument>(
                Builders<ReviewStateDocument>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.QuestionId),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<ReviewStateDocument>(
                Builders<ReviewStateDocument>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.DueAt)),
        ], cancellationToken);
    }
}
