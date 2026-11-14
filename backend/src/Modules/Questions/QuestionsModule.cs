using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

public sealed class QuestionsModule : IModule
{
    public string Name => "questions";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IQuestionCatalog, QuestionCatalog>();
        services.AddSingleton<IQuestionGrader, StrictChoiceGrader>();
        services.AddSingleton<QuestionPoolStore>();
        services.AddScoped<ICardFactory, DeterministicCardFactory>();
        services.AddScoped<QuestionService>();
        services.AddScoped<QuestionPoolService>();
        services.AddScoped<QuestionImportService>();
        services.AddSingleton<ImportPlanCache>();
        services.AddSingleton<ImportJobStore>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapQuestionEndpoints();
        app.MapQuestionPoolEndpoints();
        app.MapQuestionImportEndpoints();
    }

    public async Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var provider = services.GetRequiredService<MongoDatabaseProvider>();
        var questions = provider.Collection<QuestionDocument>(QuestionCollections.Questions);
        var revisions = provider.Collection<QuestionRevisionDocument>(QuestionCollections.Revisions);

        await questions.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<QuestionDocument>(
                    Builders<QuestionDocument>.IndexKeys
                        .Ascending(x => x.BankId)
                        .Ascending(x => x.Status)),
                new CreateIndexModel<QuestionDocument>(
                    Builders<QuestionDocument>.IndexKeys
                        .Ascending(x => x.BankId)
                        .Ascending(x => x.ContentHash)),
                new CreateIndexModel<QuestionDocument>(
                    Builders<QuestionDocument>.IndexKeys
                        .Ascending(x => x.BankId)
                        .Ascending("distractorRule.poolId")),
                new CreateIndexModel<QuestionDocument>(
                    Builders<QuestionDocument>.IndexKeys.Ascending("importBatchId")),
            ],
            cancellationToken);

        var batches = provider.Collection<ImportBatchDocument>(ImportCollections.Batches);
        await batches.Indexes.CreateOneAsync(
            new CreateIndexModel<ImportBatchDocument>(
                Builders<ImportBatchDocument>.IndexKeys
                    .Ascending(x => x.BankId)
                    .Descending(x => x.CreatedAt)),
            cancellationToken: cancellationToken);

        var batchItems = provider.Collection<ImportBatchItemRecord>(ImportCollections.BatchItems);
        await batchItems.Indexes.CreateOneAsync(
            new CreateIndexModel<ImportBatchItemRecord>(
                Builders<ImportBatchItemRecord>.IndexKeys
                    .Ascending(x => x.BatchId)
                    .Ascending(x => x.Index)),
            cancellationToken: cancellationToken);

        await revisions.Indexes.CreateOneAsync(
            new CreateIndexModel<QuestionRevisionDocument>(
                Builders<QuestionRevisionDocument>.IndexKeys
                    .Ascending(x => x.QuestionId)
                    .Ascending(x => x.Version),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

        var pools = provider.Collection<OptionPoolDocument>(QuestionPoolCollections.Pools);
        await pools.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<OptionPoolDocument>(
                    Builders<OptionPoolDocument>.IndexKeys
                        .Ascending(x => x.BankId)
                        .Ascending(x => x.DeletedAt)),
            ],
            cancellationToken);

        var entries = provider.Collection<OptionPoolEntryDocument>(QuestionPoolCollections.Entries);
        await entries.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<OptionPoolEntryDocument>(
                    Builders<OptionPoolEntryDocument>.IndexKeys
                        .Ascending(x => x.PoolId)
                        .Ascending(x => x.Disabled)),
            ],
            cancellationToken);

        await QuestionSeeder.SeedAsync(services, questions, cancellationToken);
    }
}
