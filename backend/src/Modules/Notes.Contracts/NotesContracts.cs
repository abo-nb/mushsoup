using System.Text.Json;

namespace Notes.Contracts;

public static class NoteVisibility
{
    public const string Public = "public";
    public const string Private = "private";
}

public sealed record NoteSummaryDto(
    string Id,
    string OwnerId,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Tags,
    string Visibility,
    string Year,
    string Semester,
    int SectionCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<string> Chapters);

public sealed record NoteDto(
    string Id,
    string OwnerId,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Tags,
    string Visibility,
    string Year,
    string Semester,
    JsonElement Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<string> Chapters);

public sealed record NoteMetadataRequest(
    string? Visibility,
    IReadOnlyList<string>? Tags,
    string? Year = null,
    string? Semester = null);

/// <summary>讲义导入提示词：只描述格式，不规定内容。</summary>
public sealed record NoteImportPromptDto(string Prompt);

/// <summary>跨模块只读的讲义统计。</summary>
public interface INoteDirectory
{
    Task<IReadOnlyDictionary<string, int>> CountOwnedByUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>公开且未删除，用于短链接与游客访问的准入判断。</summary>
    Task<bool> IsPublicAsync(string noteId, CancellationToken cancellationToken = default);
}
