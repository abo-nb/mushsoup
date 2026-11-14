using BuildingBlocks.Infrastructure;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

internal sealed class QuestionPoolStore(MongoDatabaseProvider provider)
{
    private IMongoCollection<OptionPoolDocument> Pools =>
        provider.Collection<OptionPoolDocument>(QuestionPoolCollections.Pools);

    private IMongoCollection<OptionPoolEntryDocument> Entries =>
        provider.Collection<OptionPoolEntryDocument>(QuestionPoolCollections.Entries);

    private IMongoCollection<QuestionDocument> Questions =>
        provider.Collection<QuestionDocument>(QuestionCollections.Questions);

    public async Task<List<OptionPoolDocument>> GetPoolsAsync(string bankId, CancellationToken cancellationToken)
    {
        var filter = Builders<OptionPoolDocument>.Filter.Eq(x => x.BankId, bankId)
                     & Builders<OptionPoolDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null);
        return await Pools
            .Find(filter)
            .Sort(Builders<OptionPoolDocument>.Sort.Ascending(x => x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OptionPoolDocument?> GetPoolAsync(string poolId, CancellationToken cancellationToken)
    {
        var filter = Builders<OptionPoolDocument>.Filter.Eq(x => x.Id, poolId)
                     & Builders<OptionPoolDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null);
        return await Pools.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<OptionPoolEntryDocument>> GetEntriesAsync(
        string poolId,
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        var filter = Builders<OptionPoolEntryDocument>.Filter.Eq(x => x.PoolId, poolId);
        if (!includeDisabled)
        {
            filter &= Builders<OptionPoolEntryDocument>.Filter.Eq(x => x.Disabled, false);
        }

        return await Entries
            .Find(filter)
            .Sort(Builders<OptionPoolEntryDocument>.Sort.Ascending(x => x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<long> CountAvailableAsync(string poolId, CancellationToken cancellationToken) =>
        Entries.CountDocumentsAsync(
            x => x.PoolId == poolId && !x.Disabled,
            cancellationToken: cancellationToken);

    /// <summary>该题库里引用这个池的干扰项规则。</summary>
    public async Task<List<DistractorRuleDocument>> GetReferencingRulesAsync(
        string bankId,
        string poolId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<QuestionDocument>.Filter.Eq(x => x.BankId, bankId)
                     & Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null)
                     & Builders<QuestionDocument>.Filter.Eq("distractorRule.poolId", poolId);

        var questions = await Questions.Find(filter).ToListAsync(cancellationToken);
        return questions
            .Select(question => question.DistractorRule)
            .Where(rule => rule is not null)
            .Select(rule => rule!)
            .ToList();
    }

    /// <summary>引用该池的题目里，最严格的一条需要多少条可用选项（含 1 条余量）。</summary>
    public async Task<int> MaxRequiredAsync(string bankId, string poolId, CancellationToken cancellationToken)
    {
        var rules = await GetReferencingRulesAsync(bankId, poolId, cancellationToken);
        return rules.Count == 0 ? 0 : rules.Max(rule => rule.Count) + 1;
    }

    /// <summary>归一化后的文本，用于排除与题干选项重复的池条目。</summary>
    public static string Normalize(string text) => QuestionNormalizer.Normalize(text);
}
