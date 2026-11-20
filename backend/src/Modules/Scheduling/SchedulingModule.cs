using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
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
        services.AddScoped<IReviewScheduler, ReviewScheduler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // 调度只被 Practice 通过接口调用，P1 不对外暴露端点。
    }

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var database = services.GetRequiredService<MongoDatabaseProvider>();
        var states = database.Collection<ReviewStateDocument>(SchedulingCollections.ReviewStates);

        // 旧版本在 (UserId, QuestionId) 上建过唯一索引；现在每种算法各存一份状态，先把它拆掉。
        var legacy = new List<string>();
        using (var cursor = await states.Indexes.ListAsync(cancellationToken))
        {
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var index in cursor.Current)
                {
                    if (index.GetValue("key", BsonNull.Value) is BsonDocument key
                        && key.ElementCount == 2
                        && key.Contains("UserId")
                        && key.Contains("QuestionId"))
                    {
                        legacy.Add(index["name"].AsString);
                    }
                }
            }
        }

        foreach (var name in legacy)
        {
            await states.Indexes.DropOneAsync(name, cancellationToken);
        }

        await states.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ReviewStateDocument>(
                Builders<ReviewStateDocument>.IndexKeys
                    .Ascending(x => x.UserId)
                    .Ascending(x => x.QuestionId)
                    .Ascending(x => x.Algorithm),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<ReviewStateDocument>(
                Builders<ReviewStateDocument>.IndexKeys
                    .Ascending(x => x.UserId)
                    .Ascending(x => x.Algorithm)
                    .Ascending(x => x.DueAt)),
        ], cancellationToken);
    }
}
