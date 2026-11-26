namespace Banks.Contracts;

public static class BankVisibility
{
    public const string Private = "private";
    public const string Public = "public";
}

public sealed record BankSettingsDto(bool DefaultShuffleOptions, bool RequireReview)
{
    public static readonly BankSettingsDto Default = new(true, false);
}

public sealed record BankDto(
    string Id,
    string ClassId,
    string OwnerId,
    string Name,
    string? Description,
    string Visibility,
    IReadOnlyList<string> Tags,
    string Year,
    string Semester,
    BankSettingsDto Settings,
    IReadOnlyList<string> MemberIds,
    int QuestionCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    /// <summary>创建者的 Discord 显示名；只有列表接口会填，单条接口可能为 null。</summary>
    string? OwnerName = null);

public sealed record BankAccessDto(BankDto Bank, bool IsOwner, bool IsMember, bool CanView, bool CanEdit);

public sealed record CreateBankRequest(
    string Name,
    string? Description,
    IReadOnlyList<string>? Tags = null,
    string? Year = null,
    string? Semester = null);

public sealed record UpdateBankRequest(
    string? Name,
    string? Description,
    string? Visibility,
    BankSettingsDto? Settings,
    IReadOnlyList<string>? Tags = null,
    string? Year = null,
    string? Semester = null);

public sealed record AddBankMemberRequest(string UserId);

public interface IBankDirectory
{
    Task<BankAccessDto?> GetAccessAsync(string bankId, string userId, string? classId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BankDto>> ListVisibleAsync(string userId, string? classId, CancellationToken cancellationToken = default);

    Task<bool> CanEditAsync(string bankId, string userId, CancellationToken cancellationToken = default);

    /// <summary>批量导入时一次性改写题库设置；只有创建者能调用。</summary>
    Task<BankDto> UpdateAsync(
        string bankId,
        UpdateBankRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>题目增删时由 Questions 模块调用，维护反规范化的题量。</summary>
    Task TouchQuestionCountAsync(string bankId, int delta, CancellationToken cancellationToken = default);

    /// <summary>按创建者统计未删除的题库数。</summary>
    Task<IReadOnlyDictionary<string, int>> CountOwnedByUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>当前所有公开、未删除的题库 id，供跨模块过滤公开内容。</summary>
    Task<IReadOnlyCollection<string>> ListPublicIdsAsync(CancellationToken cancellationToken = default);
}
