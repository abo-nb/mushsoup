using System.Text.Json;

namespace Notes;

/// <summary>章节名只给列表和详情展示用，从 contentJson 的 sections 派生，不落库。</summary>
internal static class NoteChapters
{
    public static IReadOnlyList<string> FromContent(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object ||
            !content.TryGetProperty("sections", out var sections) ||
            sections.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var chapters = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in sections.EnumerateArray())
        {
            if (section.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var chapter = Build(ReadText(section, "label"), ReadText(section, "title"));
            if (chapter is not null && seen.Add(chapter))
            {
                chapters.Add(chapter);
            }
        }

        return chapters;
    }

    public static IReadOnlyList<string> FromJson(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(contentJson);
        return FromContent(document.RootElement);
    }

    private static string? Build(string? label, string? title) => (label, title) switch
    {
        (null, null) => null,
        (null, _) => title,
        (_, null) => label,
        _ => $"{label} {title}",
    };

    private static string? ReadText(JsonElement owner, string propertyName)
    {
        if (!owner.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
