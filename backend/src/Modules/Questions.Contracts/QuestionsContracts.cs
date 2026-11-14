namespace Questions.Contracts;

public static class QuestionTypes
{
    public const string SingleChoice = "single_choice";
    public const string MultipleChoice = "multiple_choice";

    public static bool IsKnown(string type) => type is SingleChoice or MultipleChoice;
}

public static class QuestionStatus
{
    public const string Published = "published";
    public const string Pending = "pending";
    public const string Archived = "archived";
}

public static class ShuffleMode
{
    public const string Follow = "follow";
    public const string Always = "always";
    public const string Never = "never";
}

public sealed record QuestionOptionDto(string Key, string Text, bool Correct);

/// <summary>从选项池抽取 Count 个干扰项，与题目自带的选项一起组成一张卡。</summary>
public sealed record DistractorRuleDto(string PoolId, int Count);

public sealed record QuestionDto(
    string Id,
    string BankId,
    string Type,
    string Stem,
    IReadOnlyList<QuestionOptionDto> Options,
    string Shuffle,
    IReadOnlyList<string> Tags,
    int Difficulty,
    string? Source,
    string Explanation,
    string Status,
    bool Unreviewed,
    int Version,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<string> ChapterPath,
    DistractorRuleDto? DistractorRule = null);

public sealed record QuestionSummaryDto(
    string Id,
    string Stem,
    string Type,
    IReadOnlyList<string> Tags,
    int Difficulty,
    int OptionCount,
    string Status,
    DateTime UpdatedAt,
    IReadOnlyList<string> ChapterPath,
    bool UsesPool = false);

/// <summary>一个题库用到的「大章」（ChapterPath 第一层），供公开页「包含章」筛选用。</summary>
public sealed record BankChapterIndexDto(string BankId, IReadOnlyList<string> Chapters);

public sealed record OptionInput(string Key, string Text);

public sealed record QuestionRevisionDto(int Version, string Type, string Stem, string EditedBy, DateTime EditedAt);

/// <summary>Answer 是正确选项的 key 列表；单选恰好 1 个，多选至少 2 个。ChapterPath 是章节路径，最多 3 层。</summary>
public sealed record SaveQuestionRequest(
    string Type,
    string Stem,
    IReadOnlyList<OptionInput> Options,
    IReadOnlyList<string> Answer,
    string? Shuffle = null,
    IReadOnlyList<string>? Tags = null,
    int? Difficulty = null,
    string? Source = null,
    string? Explanation = null,
    DistractorRuleDto? DistractorRule = null,
    IReadOnlyList<string>? ChapterPath = null);

public sealed record GradeResult(bool Correct, double Score, IReadOnlyDictionary<string, string> PerOption);

public sealed record CardOptionDto(string Key, string Label, string Text);

/// <summary>发给答题者的一张卡：不含 correct 标记，顺序已按 seed 打乱，Label 是展示用的 A/B/C/D。</summary>
public sealed record CardDto(
    string QuestionId,
    string Type,
    string Stem,
    IReadOnlyList<CardOptionDto> Options,
    string Seed);

public interface IQuestionCatalog
{
    Task<QuestionDto?> GetAsync(string questionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuestionDto>> GetManyAsync(IEnumerable<string> questionIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuestionDto>> GetByBankAsync(string bankId, CancellationToken cancellationToken = default);

    Task<int> CountByBankAsync(string bankId, CancellationToken cancellationToken = default);

    /// <summary>按创建者统计题目数，含 pending。</summary>
    Task<IReadOnlyDictionary<string, int>> CountCreatedByUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);
}

public interface ICardFactory
{
    /// <summary>确定性乱序：同一 (用户, 题, 第几次) 得到同一顺序。池选项也按同一 seed 抽取。</summary>
    Task<CardDto> BuildAsync(
        QuestionDto question,
        string userId,
        int attemptNo,
        bool shuffle,
        CancellationToken cancellationToken = default);
}

public interface IQuestionGrader
{
    GradeResult Grade(QuestionDto question, IReadOnlyCollection<string> selectedKeys);
}

// ---------- 选项池 ----------

public sealed record OptionPoolDto(
    string Id,
    string BankId,
    string Name,
    string? Description,
    int EntryCount,
    int AvailableCount,
    DateTime CreatedAt);

public sealed record PoolEntryDto(
    string Id,
    string PoolId,
    string Text,
    IReadOnlyList<string> Tags,
    bool Disabled,
    DateTime CreatedAt);

public sealed record PoolDetailDto(OptionPoolDto Pool, IReadOnlyList<PoolEntryDto> Entries);

public sealed record CreatePoolRequest(string Name, string? Description);

public sealed record UpdatePoolRequest(string? Name, string? Description);

public sealed record CreatePoolEntryRequest(string Text, IReadOnlyList<string>? Tags);

public sealed record UpdatePoolEntryRequest(string? Text, IReadOnlyList<string>? Tags, bool? Disabled);

public sealed record PoolHealthDto(
    string PoolId,
    string Name,
    int EntryCount,
    int AvailableCount,
    int ReferencingQuestions,
    int RequiredEntries,
    bool CanSample,
    IReadOnlyList<string> Warnings);
