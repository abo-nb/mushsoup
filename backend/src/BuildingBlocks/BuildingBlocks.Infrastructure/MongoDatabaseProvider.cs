using System.Collections.Concurrent;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace BuildingBlocks.Infrastructure;

public sealed class MongoDatabaseProvider
{
    private readonly ConcurrentDictionary<(Type Type, string Name), object> collections = new();
    private readonly MongoUsageGuard usage;

    public MongoDatabaseProvider(MongoOptions options, MongoUsageGuard usage)
    {
        this.usage = usage;

        var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
        settings.ClusterConfigurator = builder =>
            builder.Subscribe<CommandSucceededEvent>(usage.RecordCommand);

        Client = new MongoClient(settings);
        Database = Client.GetDatabase(options.Database);
    }

    public IMongoClient Client { get; }

    public IMongoDatabase Database { get; }

    public IMongoCollection<T> Collection<T>(string name)
    {
        var key = (typeof(T), name);
        if (collections.TryGetValue(key, out var cached))
        {
            return (IMongoCollection<T>)cached;
        }

        var wrapped = GuardedMongoCollection<T>.Wrap(Database.GetCollection<T>(name), usage);
        return (IMongoCollection<T>)collections.GetOrAdd(key, wrapped);
    }
}
