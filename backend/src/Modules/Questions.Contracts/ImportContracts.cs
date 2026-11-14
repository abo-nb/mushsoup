namespace Questions.Contracts;

public static class ImportIssueCodes
{
    public const string JsonSyntax = "E_JSON_SYNTAX";
    public const string Schema = "E_SCHEMA";
    public const string EmptyStem = "E_EMPTY_STEM";
    public const string OptionDupKey = "E_OPTION_DUP_KEY";
    public const string AnswerKeyMissing = "E_ANSWER_KEY_MISSING";
    public const string SingleAnswerCount = "E_SINGLE_ANSWER_COUNT";
    public const string MultipleAnswerCount = "E_MULTIPLE_ANSWER_COUNT";
    public const string UnknownTag = "W_UNKNOWN_TAG";
    public const string OptionLengthBias = "W_OPTION_LENGTH_BIAS";
    public const string AnswerPositionBias = "W_ANSWER_POSITION_BIAS";
    public const string BrokenLatex = "W_BROKEN_LATEX";
    public const string MissingExplanation = "W_MISSING_EXPLANATION";
    public const string Duplicate = "W_DUPLICATE";
    public const string PoolNotFound = "E_POOL_NOT_FOUND";
    public const string PoolCount = "E_POOL_COUNT";
    public const string PoolCapacity = "E_POOL_CAPACITY";
    public const string FormatVersion = "W_FORMAT_VERSION";
    public const string Difficulty = "W_DIFFICULTY";
    public const string Truncated = "W_TRUNCATED";
}

public static class ImportSeverity
{
    public const string Error = "error";
    public const string Warning = "warning";
}

public sealed record ImportIssueDto(string Code, string Message, string Severity, int? Index = null);

public sealed record ImportQuestionPreviewDto(
    int Index,
    string? ClientId,
    string Stem,
    string Status,
    IReadOnlyList<ImportIssueDto> Issues,
    IReadOnlyList<string>? ChapterPath = null,
    string? PoolName = null,
    int? PoolCount = null);

public sealed record ImportPreviewDto(
    int Total,
    int Valid,
    int Warnings,
    int Errors,
    IReadOnlyList<ImportQuestionPreviewDto> Questions,
    IReadOnlyList<ImportIssueDto> BatchIssues,
    IReadOnlyList<CardDto> SampleCards,
    string PlanId = "");

public sealed record ImportRequest(string Payload, bool Force = false);

public sealed record ImportResultDto(
    string BatchId,
    int Total,
    int Imported,
    int Skipped,
    int Failed,
    IReadOnlyList<ImportIssueDto> BatchIssues,
    IReadOnlyList<ImportQuestionPreviewDto> Items);

public sealed record ImportBatchSummaryDto(
    string Id,
    string BankId,
    string CreatedBy,
    DateTime CreatedAt,
    int Total,
    int Imported,
    int Skipped,
    int Failed,
    int Pending);

public sealed record ImportBatchDetailDto(
    ImportBatchSummaryDto Batch,
    IReadOnlyList<ImportQuestionPreviewDto> Items);

public sealed record ImportApproveResultDto(int Published, int Remaining);

public sealed record ImportPromptDto(string BankName, string Prompt, int TagCount);

public sealed record ImportJobStartRequest(string? PlanId = null, string? Payload = null, bool Force = false);

public sealed record ImportJobStartedDto(string JobId);

public sealed record ImportJobDto(
    string JobId, string Status, int Total, int Processed, int Imported, int Skipped, int Failed, int Pending,
    string? BatchId, string? Error, DateTime CreatedAt, DateTime? FinishedAt);

public static class ImportJobStatus
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}
