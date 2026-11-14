using BuildingBlocks.Infrastructure;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

internal sealed class QuestionCatalog(MongoDatabaseProvider provider) : IQuestionCatalog
{
    private IMongoCollection<QuestionDocument> Questions =>
        provider.Collection<QuestionDocument>(QuestionCollections.Questions);

    private static FilterDefinition<QuestionDocument> NotDeleted =>
        Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null);

    public async Task<QuestionDto?> GetAsync(string questionId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.Id, questionId) & NotDeleted;
        var document = await Questions.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : QuestionMapping.ToDto(document);
    }

    public async Task<IReadOnlyList<QuestionDto>> GetManyAsync(
        IEnumerable<string> questionIds,
        CancellationToken cancellationToken = default)
    {
        var ids = questionIds.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var filter = Builders<QuestionDocument>.Filter.In(x => x.Id, ids) & NotDeleted;
        var documents = await Questions.Find(filter).ToListAsync(cancellationToken);
        var byId = documents.ToDictionary(x => x.Id, StringComparer.Ordinal);

        return ids
            .Where(byId.ContainsKey)
            .Select(id => QuestionMapping.ToDto(byId[id]))
            .ToList();
    }

    public async Task<IReadOnlyList<QuestionDto>> GetByBankAsync(
        string bankId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.BankId, bankId)
                     & NotDeleted
                     & Builders<QuestionDocument>.Filter.Ne(x => x.Status, QuestionStatus.Archived);
        var documents = await Questions
            .Find(filter)
            .Sort(Builders<QuestionDocument>.Sort.Descending(x => x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return documents.Select(QuestionMapping.ToDto).ToList();
    }

    public async Task<int> CountByBankAsync(string bankId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.BankId, bankId)
                     & NotDeleted
                     & Builders<QuestionDocument>.Filter.Ne(x => x.Status, QuestionStatus.Archived);
        var count = await Questions.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return (int)count;
    }

    public async Task<IReadOnlyDictionary<string, int>> CountCreatedByUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var creators = await Questions
            .Find(Builders<QuestionDocument>.Filter.In(x => x.CreatedBy, ids) & NotDeleted)
            .Project(x => x.CreatedBy)
            .ToListAsync(cancellationToken);

        return creators
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }
}
