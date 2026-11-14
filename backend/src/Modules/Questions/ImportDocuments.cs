using BuildingBlocks.Contracts;
using MongoDB.Bson.Serialization.Attributes;
using Questions.Contracts;

namespace Questions;

internal static class ImportCollections
{
    public const string Batches = "import_batches";
    public const string BatchItems = "import_batch_items";
}

internal sealed class ImportBatchDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    [BsonElement("bankId")]
    public string BankId { get; set; } = "";

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = "";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("total")]
    public int Total { get; set; }

    [BsonElement("imported")]
    public int Imported { get; set; }

    [BsonElement("skipped")]
    public int Skipped { get; set; }

    [BsonElement("failed")]
    public int Failed { get; set; }

    [BsonElement("pending")]
    public int Pending { get; set; }

    [BsonElement("batchIssues")]
    public List<ImportIssueDocument> BatchIssues { get; set; } = [];

    [BsonElement("items")]
    public List<ImportBatchItemDocument> Items { get; set; } = [];
}

internal sealed class ImportBatchItemDocument
{
    [BsonElement("questionId")]
    public string? QuestionId { get; set; }

    [BsonElement("clientId")]
    public string? ClientId { get; set; }

    [BsonElement("stem")]
    public string Stem { get; set; } = "";

    [BsonElement("chapterPath")]
    public List<string> ChapterPath { get; set; } = [];

    [BsonElement("status")]
    public string Status { get; set; } = "";

    [BsonElement("issues")]
    public List<ImportIssueDocument> Issues { get; set; } = [];
}

internal sealed class ImportIssueDocument
{
    [BsonElement("code")]
    public string Code { get; set; } = "";

    [BsonElement("message")]
    public string Message { get; set; } = "";

    [BsonElement("severity")]
    public string Severity { get; set; } = "warning";

    [BsonElement("index")]
    public int? Index { get; set; }
}

internal sealed class ImportBatchItemRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    [BsonElement("batchId")]
    public string BatchId { get; set; } = "";

    [BsonElement("index")]
    public int Index { get; set; }

    [BsonElement("questionId")]
    public string? QuestionId { get; set; }

    [BsonElement("clientId")]
    public string? ClientId { get; set; }

    [BsonElement("stem")]
    public string Stem { get; set; } = "";

    [BsonElement("chapterPath")]
    public List<string> ChapterPath { get; set; } = [];

    [BsonElement("status")]
    public string Status { get; set; } = "";

    [BsonElement("issues")]
    public List<ImportIssueDocument> Issues { get; set; } = [];

    public static ImportBatchItemRecord From(string batchId, int index, ImportBatchItemDocument item) => new()
    {
        Id = Ids.New(),
        BatchId = batchId,
        Index = index,
        QuestionId = item.QuestionId,
        ClientId = item.ClientId,
        Stem = item.Stem,
        ChapterPath = item.ChapterPath.ToList(),
        Status = item.Status,
        Issues = item.Issues.ToList(),
    };

    public ImportBatchItemDocument ToItem() => new()
    {
        QuestionId = QuestionId,
        ClientId = ClientId,
        Stem = Stem,
        ChapterPath = ChapterPath.ToList(),
        Status = Status,
        Issues = Issues.ToList(),
    };
}

internal static class ImportMapping
{
    public static ImportIssueDto ToDto(ImportIssueDocument document) =>
        new(document.Code, document.Message, document.Severity, document.Index);

    public static ImportIssueDocument ToDocument(Questions.Contracts.ImportIssueDto dto) => new()
    {
        Code = dto.Code,
        Message = dto.Message,
        Severity = dto.Severity,
        Index = dto.Index,
    };
}
