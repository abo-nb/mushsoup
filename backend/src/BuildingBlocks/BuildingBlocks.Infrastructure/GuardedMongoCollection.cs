using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// 手写的 <see cref="IMongoCollection{TDocument}"/> 包装：所有成员原样转发给真实的集合，
/// 只在读、写操作真正发起前先做一次额度预检。
/// </summary>
internal sealed class GuardedMongoCollection<TDocument> : IMongoCollection<TDocument>
{
    private readonly IMongoCollection<TDocument> inner;
    private readonly MongoUsageGuard guard;

    private GuardedMongoCollection(IMongoCollection<TDocument> inner, MongoUsageGuard guard)
    {
        this.inner = inner;
        this.guard = guard;
    }

    public static IMongoCollection<TDocument> Wrap(IMongoCollection<TDocument> inner, MongoUsageGuard guard) =>
        new GuardedMongoCollection<TDocument>(inner, guard);

    public CollectionNamespace CollectionNamespace => inner.CollectionNamespace;

    public IMongoDatabase Database => inner.Database;

    public IBsonSerializer<TDocument> DocumentSerializer => inner.DocumentSerializer;

    public IMongoIndexManager<TDocument> Indexes => inner.Indexes;

    public IMongoSearchIndexManager SearchIndexes => inner.SearchIndexes;

    public MongoCollectionSettings Settings => inner.Settings;

    public IAsyncCursor<TResult> Aggregate<TResult>(
        PipelineDefinition<TDocument, TResult> pipeline,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.Aggregate(pipeline, options, cancellationToken);
    }

    public IAsyncCursor<TResult> Aggregate<TResult>(
        IClientSessionHandle session,
        PipelineDefinition<TDocument, TResult> pipeline,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.Aggregate(session, pipeline, options, cancellationToken);
    }

    public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
        PipelineDefinition<TDocument, TResult> pipeline,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.AggregateAsync(pipeline, options, cancellationToken);
    }

    public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
        IClientSessionHandle session,
        PipelineDefinition<TDocument, TResult> pipeline,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.AggregateAsync(session, pipeline, options, cancellationToken);
    }

    public void AggregateToCollection<TResult>(
        PipelineDefinition<TDocument, TResult> pipeline,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        inner.AggregateToCollection(pipeline, options, cancellationToken);
    }

    public void AggregateToCollection<TResult>(
        IClientSessionHandle session,
        PipelineDefinition<TDocument, TResult> pipeline,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        inner.AggregateToCollection(session, pipeline, options, cancellationToken);
    }

    public Task AggregateToCollectionAsync<TResult>(
        PipelineDefinition<TDocument, TResult> pipeline,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.AggregateToCollectionAsync(pipeline, options, cancellationToken);
    }

    public Task AggregateToCollectionAsync<TResult>(
        IClientSessionHandle session,
        PipelineDefinition<TDocument, TResult> pipeline,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.AggregateToCollectionAsync(session, pipeline, options, cancellationToken);
    }

    public BulkWriteResult<TDocument> BulkWrite(
        IEnumerable<WriteModel<TDocument>> requests,
        BulkWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.BulkWrite(requests, options, cancellationToken);
    }

    public BulkWriteResult<TDocument> BulkWrite(
        IClientSessionHandle session,
        IEnumerable<WriteModel<TDocument>> requests,
        BulkWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.BulkWrite(session, requests, options, cancellationToken);
    }

    public Task<BulkWriteResult<TDocument>> BulkWriteAsync(
        IEnumerable<WriteModel<TDocument>> requests,
        BulkWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.BulkWriteAsync(requests, options, cancellationToken);
    }

    public Task<BulkWriteResult<TDocument>> BulkWriteAsync(
        IClientSessionHandle session,
        IEnumerable<WriteModel<TDocument>> requests,
        BulkWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.BulkWriteAsync(session, requests, options, cancellationToken);
    }

    [Obsolete("Use CountDocuments or EstimatedDocumentCount instead.")]
    public long Count(
        FilterDefinition<TDocument> filter,
        CountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.Count(filter, options, cancellationToken);
    }

    [Obsolete("Use CountDocuments or EstimatedDocumentCount instead.")]
    public long Count(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        CountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.Count(session, filter, options, cancellationToken);
    }

    [Obsolete("Use CountDocumentsAsync or EstimatedDocumentCountAsync instead.")]
    public Task<long> CountAsync(
        FilterDefinition<TDocument> filter,
        CountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.CountAsync(filter, options, cancellationToken);
    }

    [Obsolete("Use CountDocumentsAsync or EstimatedDocumentCountAsync instead.")]
    public Task<long> CountAsync(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        CountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.CountAsync(session, filter, options, cancellationToken);
    }

    public long CountDocuments(
        FilterDefinition<TDocument> filter,
        CountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.CountDocuments(filter, options, cancellationToken);
    }

    public long CountDocuments(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        CountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.CountDocuments(session, filter, options, cancellationToken);
    }

    public Task<long> CountDocumentsAsync(
        FilterDefinition<TDocument> filter,
        CountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.CountDocumentsAsync(filter, options, cancellationToken);
    }

    public Task<long> CountDocumentsAsync(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        CountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.CountDocumentsAsync(session, filter, options, cancellationToken);
    }

    public DeleteResult DeleteMany(
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteMany(filter, cancellationToken);
    }

    public DeleteResult DeleteMany(
        FilterDefinition<TDocument> filter,
        DeleteOptions options,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteMany(filter, options, cancellationToken);
    }

    public DeleteResult DeleteMany(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        DeleteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteMany(session, filter, options, cancellationToken);
    }

    public Task<DeleteResult> DeleteManyAsync(
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteManyAsync(filter, cancellationToken);
    }

    public Task<DeleteResult> DeleteManyAsync(
        FilterDefinition<TDocument> filter,
        DeleteOptions options,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteManyAsync(filter, options, cancellationToken);
    }

    public Task<DeleteResult> DeleteManyAsync(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        DeleteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteManyAsync(session, filter, options, cancellationToken);
    }

    public DeleteResult DeleteOne(
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteOne(filter, cancellationToken);
    }

    public DeleteResult DeleteOne(
        FilterDefinition<TDocument> filter,
        DeleteOptions options,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteOne(filter, options, cancellationToken);
    }

    public DeleteResult DeleteOne(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        DeleteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteOne(session, filter, options, cancellationToken);
    }

    public Task<DeleteResult> DeleteOneAsync(
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteOneAsync(filter, cancellationToken);
    }

    public Task<DeleteResult> DeleteOneAsync(
        FilterDefinition<TDocument> filter,
        DeleteOptions options,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteOneAsync(filter, options, cancellationToken);
    }

    public Task<DeleteResult> DeleteOneAsync(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        DeleteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.DeleteOneAsync(session, filter, options, cancellationToken);
    }

    public IAsyncCursor<TField> Distinct<TField>(
        FieldDefinition<TDocument, TField> field,
        FilterDefinition<TDocument> filter,
        DistinctOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.Distinct(field, filter, options, cancellationToken);
    }

    public IAsyncCursor<TField> Distinct<TField>(
        IClientSessionHandle session,
        FieldDefinition<TDocument, TField> field,
        FilterDefinition<TDocument> filter,
        DistinctOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.Distinct(session, field, filter, options, cancellationToken);
    }

    public Task<IAsyncCursor<TField>> DistinctAsync<TField>(
        FieldDefinition<TDocument, TField> field,
        FilterDefinition<TDocument> filter,
        DistinctOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.DistinctAsync(field, filter, options, cancellationToken);
    }

    public Task<IAsyncCursor<TField>> DistinctAsync<TField>(
        IClientSessionHandle session,
        FieldDefinition<TDocument, TField> field,
        FilterDefinition<TDocument> filter,
        DistinctOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.DistinctAsync(session, field, filter, options, cancellationToken);
    }

    public IAsyncCursor<TItem> DistinctMany<TItem>(
        FieldDefinition<TDocument, IEnumerable<TItem>> field,
        FilterDefinition<TDocument> filter,
        DistinctOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.DistinctMany(field, filter, options, cancellationToken);
    }

    public IAsyncCursor<TItem> DistinctMany<TItem>(
        IClientSessionHandle session,
        FieldDefinition<TDocument, IEnumerable<TItem>> field,
        FilterDefinition<TDocument> filter,
        DistinctOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.DistinctMany(session, field, filter, options, cancellationToken);
    }

    public Task<IAsyncCursor<TItem>> DistinctManyAsync<TItem>(
        FieldDefinition<TDocument, IEnumerable<TItem>> field,
        FilterDefinition<TDocument> filter,
        DistinctOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.DistinctManyAsync(field, filter, options, cancellationToken);
    }

    public Task<IAsyncCursor<TItem>> DistinctManyAsync<TItem>(
        IClientSessionHandle session,
        FieldDefinition<TDocument, IEnumerable<TItem>> field,
        FilterDefinition<TDocument> filter,
        DistinctOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.DistinctManyAsync(session, field, filter, options, cancellationToken);
    }

    public long EstimatedDocumentCount(
        EstimatedDocumentCountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.EstimatedDocumentCount(options, cancellationToken);
    }

    public Task<long> EstimatedDocumentCountAsync(
        EstimatedDocumentCountOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.EstimatedDocumentCountAsync(options, cancellationToken);
    }

    public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
        FilterDefinition<TDocument> filter,
        FindOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.FindAsync(filter, options, cancellationToken);
    }

    public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        FindOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.FindAsync(session, filter, options, cancellationToken);
    }

    public IAsyncCursor<TProjection> FindSync<TProjection>(
        FilterDefinition<TDocument> filter,
        FindOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.FindSync(filter, options, cancellationToken);
    }

    public IAsyncCursor<TProjection> FindSync<TProjection>(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        FindOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.FindSync(session, filter, options, cancellationToken);
    }

    public TProjection FindOneAndDelete<TProjection>(
        FilterDefinition<TDocument> filter,
        FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndDelete(filter, options, cancellationToken);
    }

    public TProjection FindOneAndDelete<TProjection>(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndDelete(session, filter, options, cancellationToken);
    }

    public Task<TProjection> FindOneAndDeleteAsync<TProjection>(
        FilterDefinition<TDocument> filter,
        FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndDeleteAsync(filter, options, cancellationToken);
    }

    public Task<TProjection> FindOneAndDeleteAsync<TProjection>(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndDeleteAsync(session, filter, options, cancellationToken);
    }

    public TProjection FindOneAndReplace<TProjection>(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndReplace(filter, replacement, options, cancellationToken);
    }

    public TProjection FindOneAndReplace<TProjection>(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndReplace(session, filter, replacement, options, cancellationToken);
    }

    public Task<TProjection> FindOneAndReplaceAsync<TProjection>(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndReplaceAsync(filter, replacement, options, cancellationToken);
    }

    public Task<TProjection> FindOneAndReplaceAsync<TProjection>(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndReplaceAsync(session, filter, replacement, options, cancellationToken);
    }

    public TProjection FindOneAndUpdate<TProjection>(
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndUpdate(filter, update, options, cancellationToken);
    }

    public TProjection FindOneAndUpdate<TProjection>(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndUpdate(session, filter, update, options, cancellationToken);
    }

    public Task<TProjection> FindOneAndUpdateAsync<TProjection>(
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
    }

    public Task<TProjection> FindOneAndUpdateAsync<TProjection>(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.FindOneAndUpdateAsync(session, filter, update, options, cancellationToken);
    }

    public void InsertMany(
        IEnumerable<TDocument> documents,
        InsertManyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        inner.InsertMany(documents, options, cancellationToken);
    }

    public void InsertMany(
        IClientSessionHandle session,
        IEnumerable<TDocument> documents,
        InsertManyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        inner.InsertMany(session, documents, options, cancellationToken);
    }

    public Task InsertManyAsync(
        IEnumerable<TDocument> documents,
        InsertManyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.InsertManyAsync(documents, options, cancellationToken);
    }

    public Task InsertManyAsync(
        IClientSessionHandle session,
        IEnumerable<TDocument> documents,
        InsertManyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.InsertManyAsync(session, documents, options, cancellationToken);
    }

    public void InsertOne(
        TDocument document,
        InsertOneOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        inner.InsertOne(document, options, cancellationToken);
    }

    public void InsertOne(
        IClientSessionHandle session,
        TDocument document,
        InsertOneOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        inner.InsertOne(session, document, options, cancellationToken);
    }

    [Obsolete("Use the new overload of InsertOneAsync with an InsertOneOptions parameter instead.")]
    public Task InsertOneAsync(TDocument document, CancellationToken _cancellationToken)
    {
        GuardWrite();
        return inner.InsertOneAsync(document, _cancellationToken);
    }

    public Task InsertOneAsync(
        TDocument document,
        InsertOneOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.InsertOneAsync(document, options, cancellationToken);
    }

    public Task InsertOneAsync(
        IClientSessionHandle session,
        TDocument document,
        InsertOneOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.InsertOneAsync(session, document, options, cancellationToken);
    }

    [Obsolete("Use Aggregation pipeline instead.")]
    public IAsyncCursor<TResult> MapReduce<TResult>(
        BsonJavaScript map,
        BsonJavaScript reduce,
        MapReduceOptions<TDocument, TResult>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.MapReduce(map, reduce, options, cancellationToken);
    }

    [Obsolete("Use Aggregation pipeline instead.")]
    public IAsyncCursor<TResult> MapReduce<TResult>(
        IClientSessionHandle session,
        BsonJavaScript map,
        BsonJavaScript reduce,
        MapReduceOptions<TDocument, TResult>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.MapReduce(session, map, reduce, options, cancellationToken);
    }

    [Obsolete("Use Aggregation pipeline instead.")]
    public Task<IAsyncCursor<TResult>> MapReduceAsync<TResult>(
        BsonJavaScript map,
        BsonJavaScript reduce,
        MapReduceOptions<TDocument, TResult>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.MapReduceAsync(map, reduce, options, cancellationToken);
    }

    [Obsolete("Use Aggregation pipeline instead.")]
    public Task<IAsyncCursor<TResult>> MapReduceAsync<TResult>(
        IClientSessionHandle session,
        BsonJavaScript map,
        BsonJavaScript reduce,
        MapReduceOptions<TDocument, TResult>? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.MapReduceAsync(session, map, reduce, options, cancellationToken);
    }

    public IFilteredMongoCollection<TDerivedDocument> OfType<TDerivedDocument>()
        where TDerivedDocument : TDocument => inner.OfType<TDerivedDocument>();

    public ReplaceOneResult ReplaceOne(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        ReplaceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.ReplaceOne(filter, replacement, options, cancellationToken);
    }

    [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
    public ReplaceOneResult ReplaceOne(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        UpdateOptions options,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.ReplaceOne(filter, replacement, options, cancellationToken);
    }

    public ReplaceOneResult ReplaceOne(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        ReplaceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.ReplaceOne(session, filter, replacement, options, cancellationToken);
    }

    [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
    public ReplaceOneResult ReplaceOne(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        UpdateOptions options,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.ReplaceOne(session, filter, replacement, options, cancellationToken);
    }

    public Task<ReplaceOneResult> ReplaceOneAsync(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        ReplaceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.ReplaceOneAsync(filter, replacement, options, cancellationToken);
    }

    [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
    public Task<ReplaceOneResult> ReplaceOneAsync(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        UpdateOptions options,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.ReplaceOneAsync(filter, replacement, options, cancellationToken);
    }

    public Task<ReplaceOneResult> ReplaceOneAsync(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        ReplaceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.ReplaceOneAsync(session, filter, replacement, options, cancellationToken);
    }

    [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
    public Task<ReplaceOneResult> ReplaceOneAsync(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        UpdateOptions options,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.ReplaceOneAsync(session, filter, replacement, options, cancellationToken);
    }

    public UpdateResult UpdateMany(
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.UpdateMany(filter, update, options, cancellationToken);
    }

    public UpdateResult UpdateMany(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.UpdateMany(session, filter, update, options, cancellationToken);
    }

    public Task<UpdateResult> UpdateManyAsync(
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.UpdateManyAsync(filter, update, options, cancellationToken);
    }

    public Task<UpdateResult> UpdateManyAsync(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.UpdateManyAsync(session, filter, update, options, cancellationToken);
    }

    public UpdateResult UpdateOne(
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.UpdateOne(filter, update, options, cancellationToken);
    }

    public UpdateResult UpdateOne(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.UpdateOne(session, filter, update, options, cancellationToken);
    }

    public Task<UpdateResult> UpdateOneAsync(
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.UpdateOneAsync(filter, update, options, cancellationToken);
    }

    public Task<UpdateResult> UpdateOneAsync(
        IClientSessionHandle session,
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return inner.UpdateOneAsync(session, filter, update, options, cancellationToken);
    }

    public IChangeStreamCursor<TResult> Watch<TResult>(
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        ChangeStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.Watch(pipeline, options, cancellationToken);
    }

    public IChangeStreamCursor<TResult> Watch<TResult>(
        IClientSessionHandle session,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        ChangeStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.Watch(session, pipeline, options, cancellationToken);
    }

    public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        ChangeStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.WatchAsync(pipeline, options, cancellationToken);
    }

    public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
        IClientSessionHandle session,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        ChangeStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GuardRead();
        return inner.WatchAsync(session, pipeline, options, cancellationToken);
    }

    public IMongoCollection<TDocument> WithReadConcern(ReadConcern readConcern) =>
        Wrap(inner.WithReadConcern(readConcern), guard);

    public IMongoCollection<TDocument> WithReadPreference(ReadPreference readPreference) =>
        Wrap(inner.WithReadPreference(readPreference), guard);

    public IMongoCollection<TDocument> WithWriteConcern(WriteConcern writeConcern) =>
        Wrap(inner.WithWriteConcern(writeConcern), guard);

    private void GuardRead() => guard.EnsureRead();

    private void GuardWrite() => guard.EnsureWrite();
}
