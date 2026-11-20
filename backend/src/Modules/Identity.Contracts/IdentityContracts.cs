namespace Identity.Contracts;

/// <summary>ShuffleOptions: follow | always | never；SubmitMode: space | auto；MemoryAlgorithm: sm2 | speed | fsrs | leitner</summary>
public sealed record UserPrefsDto(string ShuffleOptions, string SubmitMode, string MemoryAlgorithm = "sm2")
{
    public static readonly UserPrefsDto Default = new("always", "space", "sm2");
}

/// <summary>用户的对外形态。Id 是 Discord 用户 ID。</summary>
public sealed record UserDto(
    string Id,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    string? ClassId,
    UserPrefsDto Prefs);

public sealed record UserSummaryDto(string Id, string DisplayName, string? ClassId, string? AvatarUrl = null);

public sealed record UpdatePrefsRequest(string? ShuffleOptions, string? SubmitMode, string? MemoryAlgorithm = null);

/// <summary>用 Discord 用户 ID 发起验证码投递。</summary>
public sealed record VerifyRequest(string UserId);

/// <summary>票据 + 验证码。票据由本站签发，不暴露上游 requestId。</summary>
public sealed record VerifyConfirmRequest(string TicketId, string Code);

/// <summary>Delivery: dm | thread</summary>
public sealed record VerifyTicketDto(string TicketId, string Delivery, DateTime ExpiresAt);

public sealed record VerifyMemberDto(
    string Id,
    string Username,
    string? GlobalName,
    string? Nickname,
    string DisplayName,
    string? AvatarUrl);

public sealed record VerifyMembersDto(int Count, IReadOnlyList<VerifyMemberDto> Members);

/// <summary>跨模块只读的用户查询。任何模块都不该直接读 users 集合。</summary>
public interface IUserDirectory
{
    Task<UserSummaryDto?> GetAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSummaryDto>> GetManyAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default);

    /// <summary>班级成员，按 DisplayName 排序。</summary>
    Task<IReadOnlyList<UserSummaryDto>> ListByClassAsync(string classId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken = default);

    Task<UserPrefsDto?> GetPrefsAsync(string userId, CancellationToken cancellationToken = default);
}
