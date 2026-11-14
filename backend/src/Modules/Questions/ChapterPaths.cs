namespace Questions;

/// <summary>章节路径：每层 trim 后丢弃空层，最多 3 层，每层不超过 40 字。</summary>
internal static class ChapterPaths
{
    public const int MaxDepth = 3;

    public const int MaxSegmentLength = 40;

    public static List<string> Normalize(IEnumerable<string?>? path)
    {
        if (path is null)
        {
            return [];
        }

        return path
            .Select(segment => segment?.Trim() ?? "")
            .Where(segment => segment.Length > 0)
            .ToList();
    }

    public static string? FindProblem(IReadOnlyList<string> path) =>
        path.Count > MaxDepth
            ? $"章节最多 {MaxDepth} 层"
            : path.FirstOrDefault(segment => segment.Length > MaxSegmentLength) is { } tooLong
                ? $"章节每层不能超过 {MaxSegmentLength} 个字符：{tooLong}"
                : null;
}
