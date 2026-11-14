using Banks.Contracts;
using BuildingBlocks.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Questions.Contracts;

namespace Questions;

internal static class QuestionSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IMongoCollection<QuestionDocument> questions,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Questions.Seed");
        var banks = services.GetRequiredService<IBankDirectory>();

        try
        {
            var hasAny = await questions
                .Find(FilterDefinition<QuestionDocument>.Empty)
                .Limit(1)
                .AnyAsync(cancellationToken);
            if (hasAny)
            {
                return;
            }

            // 题库由 Banks 模块创建，Contracts 没有“列出全部题库”的入口，只能尽力找一个空题库。
            var visible = await banks.ListVisibleAsync("questions-seed", null, cancellationToken);
            var bank = visible.FirstOrDefault(b => b.QuestionCount == 0);
            if (bank is null)
            {
                logger.LogInformation("No empty bank available, skip question seed");
                return;
            }

            var countFilter = Builders<QuestionDocument>.Filter.Eq(x => x.BankId, bank.Id)
                              & Builders<QuestionDocument>.Filter.Eq(x => x.DeletedAt, (DateTime?)null);
            if (await questions.CountDocumentsAsync(countFilter, cancellationToken: cancellationToken) > 0)
            {
                return;
            }

            var documents = BuildSeedDocuments(bank, DateTime.UtcNow);
            await questions.InsertManyAsync(documents, cancellationToken: cancellationToken);
            await banks.TouchQuestionCountAsync(bank.Id, documents.Count, cancellationToken);
            logger.LogInformation("Seeded {Count} demo questions into bank {BankId}", documents.Count, bank.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Question seed skipped");
        }
    }

    private static List<QuestionDocument> BuildSeedDocuments(BankDto bank, DateTime now)
    {
        var seeds = new[]
        {
            new SeedQuestion(
                QuestionTypes.SingleChoice,
                "下列哪个数不是质数？",
                [("A", "2"), ("B", "9"), ("C", "13"), ("D", "17")],
                ["B"],
                ShuffleMode.Follow,
                ["数学", "基础"],
                1,
                "9 = 3 × 3，是合数。"),
            new SeedQuestion(
                QuestionTypes.SingleChoice,
                "HTTP 状态码 404 表示什么？",
                [("A", "服务器内部错误"), ("B", "资源未找到"), ("C", "请求成功"), ("D", "重定向")],
                ["B"],
                ShuffleMode.Always,
                ["计算机", "网络"],
                2,
                "4xx 表示客户端错误，404 具体表示资源不存在。"),
            new SeedQuestion(
                QuestionTypes.MultipleChoice,
                "以下哪些是关系型数据库？",
                [("A", "MySQL"), ("B", "PostgreSQL"), ("C", "MongoDB"), ("D", "Redis")],
                ["A", "B"],
                ShuffleMode.Follow,
                ["计算机", "数据库"],
                3,
                "MongoDB 是文档数据库，Redis 是键值数据库。"),
            new SeedQuestion(
                QuestionTypes.MultipleChoice,
                "以下哪些属于 JavaScript 的原始类型？",
                [("A", "string"), ("B", "number"), ("C", "object"), ("D", "boolean")],
                ["A", "B", "D"],
                ShuffleMode.Always,
                ["计算机", "JavaScript"],
                3,
                "object 是引用类型，其余是原始类型。"),
            new SeedQuestion(
                QuestionTypes.SingleChoice,
                "光在真空中的传播速度约为多少？",
                [("A", "3 × 10⁸ m/s"), ("B", "3 × 10⁶ m/s"), ("C", "3 × 10⁵ m/s"), ("D", "3 × 10⁷ m/s")],
                ["A"],
                ShuffleMode.Never,
                ["物理"],
                2,
                "真空光速约为 299 792 458 m/s，通常取 3 × 10⁸ m/s。"),
            new SeedQuestion(
                QuestionTypes.SingleChoice,
                "《红楼梦》的作者是谁？",
                [("A", "曹雪芹"), ("B", "施耐庵"), ("C", "罗贯中"), ("D", "吴承恩")],
                ["A"],
                ShuffleMode.Follow,
                ["文学"],
                1,
                "《红楼梦》前八十回一般认为是曹雪芹所著。"),
            new SeedQuestion(
                QuestionTypes.MultipleChoice,
                "以下哪些是面向对象编程的基本特征？",
                [("A", "封装"), ("B", "继承"), ("C", "多态"), ("D", "编译")],
                ["A", "B", "C"],
                ShuffleMode.Follow,
                ["计算机", "编程"],
                2,
                "封装、继承、多态是面向对象的三大特征。"),
            new SeedQuestion(
                QuestionTypes.SingleChoice,
                "太阳系中体积最大的行星是哪一颗？",
                [("A", "地球"), ("B", "木星"), ("C", "土星"), ("D", "火星")],
                ["B"],
                ShuffleMode.Never,
                ["常识", "天文"],
                1,
                "木星的体积和质量都居太阳系行星之首。"),
        };

        var documents = new List<QuestionDocument>(seeds.Length);
        for (var index = 0; index < seeds.Length; index++)
        {
            var seed = seeds[index];
            var answerSet = seed.Answer.ToHashSet(StringComparer.Ordinal);
            var timestamp = now.AddMinutes(-index);
            var options = seed.Options
                .Select(option => new QuestionOptionDocument
                {
                    Key = option.Key,
                    Text = option.Text,
                    Correct = answerSet.Contains(option.Key),
                })
                .ToList();

            documents.Add(new QuestionDocument
            {
                Id = Ids.New(),
                BankId = bank.Id,
                Type = seed.Type,
                Stem = seed.Stem,
                Options = options,
                Shuffle = seed.Shuffle,
                Tags = seed.Tags.ToList(),
                Difficulty = seed.Difficulty,
                Explanation = seed.Explanation,
                Status = QuestionStatus.Published,
                Unreviewed = true,
                Version = 1,
                CreatedBy = bank.OwnerId,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
                ContentHash = QuestionNormalizer.ComputeContentHash(seed.Stem, options.Select(o => o.Text)),
            });
        }

        return documents;
    }

    private sealed record SeedQuestion(
        string Type,
        string Stem,
        (string Key, string Text)[] Options,
        string[] Answer,
        string Shuffle,
        string[] Tags,
        int Difficulty,
        string Explanation);
}
