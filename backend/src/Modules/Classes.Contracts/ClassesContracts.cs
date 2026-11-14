namespace Classes.Contracts;

public sealed record ClassDto(string Id, string Name, string InviteCode, IReadOnlyList<string> AdminIds);

public interface IClassDirectory
{
    /// <summary>未配置班级时返回 null。</summary>
    Task<ClassDto?> GetAsync(string classId, CancellationToken cancellationToken = default);

    /// <summary>按邀请码查找，注册时使用。</summary>
    Task<ClassDto?> FindByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default);

    /// <summary>系统默认班级，首个用户自动加入。</summary>
    Task<ClassDto> GetDefaultAsync(CancellationToken cancellationToken = default);

    Task<bool> IsAdminAsync(string classId, string userId, CancellationToken cancellationToken = default);
}
