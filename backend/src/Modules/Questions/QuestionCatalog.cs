using BuildingBlocks.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

internal sealed class QuestionCatalog(MongoDatabaseProvider provider, QuestionCache cache) : IQuestionCatalog
{
    private static readonly TimeSpan ItemTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SummaryListTtl = TimeSpan.FromSeconds(30);

    private static readonly BsonDocument SummaryProjection = BsonDocument.Parse(
        """
        {
            "stem": 1,
            "type": 1,
            "tags": 1,
            "difficulty": 1,
            "optionCount": { "$size": { "$ifNull": ["$options", []] } },
            "status": 1,
            "updatedAt": 1,
            "chapterPath": 1,
            "usesPool": { "$ne": [{ "$ifNull": ["$distractorRule", null] }, null] }
        }
        """);

    private IMongoCollection<QuestionDocument> Questions =>
        provider.Collection<QuestionDocument>(QuestionCollections.Questions);

    private static FilterDefinition<QuestionDocument> NotDeleted =>
        Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null);

    public async Task<QuestionDto?> GetAsync(string questionId, CancellationToken cancellationToken = default)
    {
        var key = cache.Key($"item:{questionId}");
        if (cache.TryGet(key, out QuestionDto? cached))
        {
            return cached;
        }

        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.Id, questionId) & NotDeleted;
        var document = await Questions.Find(filter).FirstOrDefaultAsync(cancellationToken);
        var dto = document is null ? null : QuestionMapping.ToDto(document);

        // 不存在的题目也缓存，避免反复穿透到 Mongo。
        cache.Set<QuestionDto?>(key, dto, ItemTtl);
        return dto;
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

    /// <summary>
    /// 公开题库的题目摘要（不含答案），只投影需要的字段，避免把整份文档和选项拉回来。
    /// </summary>
    internal async Task<IReadOnlyList<QuestionSummaryDto>> GetPublicSummariesAsync(
        string bankId,
        CancellationToken cancellationToken = default)
    {
        var key = cache.Key($"summaries:{bankId}");
        if (cache.TryGet(key, out IReadOnlyList<QuestionSummaryDto>? cached) && cached is not null)
        {
            return cached;
        }

        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.BankId, bankId)
                     & NotDeleted
                     & Builders<QuestionDocument>.Filter.Eq(x => x.Status, QuestionStatus.Published);
        var rows = await Questions
            .Find(filter)
            .Sort(Builders<QuestionDocument>.Sort.Descending(x => x.UpdatedAt))
            .Project<BsonDocument>(new BsonDocumentProjectionDefinition<QuestionDocument>(SummaryProjection))
            .ToListAsync(cancellationToken);

        IReadOnlyList<QuestionSummaryDto> result = rows.Select(ToSummary).ToList();
        cache.Set(key, result, SummaryListTtl);
        return result;
    }

    private static QuestionSummaryDto ToSummary(BsonDocument row) => new(
        row["_id"].AsString,
        row["stem"].AsString,
        row["type"].AsString,
        ReadStrings(row, "tags"),
        row.TryGetValue("difficulty", out var difficulty) ? difficulty.ToInt32() : 3,
        row.TryGetValue("optionCount", out var optionCount) ? optionCount.ToInt32() : 0,
        row["status"].AsString,
        row["updatedAt"].AsBsonDateTime.ToUniversalTime(),
        ReadStrings(row, "chapterPath"),
        row.TryGetValue("usesPool", out var usesPool) && usesPool.ToBoolean());

    private static List<string> ReadStrings(BsonDocument row, string field) =>
        row.TryGetValue(field, out var value) && value.IsBsonArray
            ? value.AsBsonArray.Where(item => item.IsString).Select(item => item.AsString).ToList()
            : [];

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
