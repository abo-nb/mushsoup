using System.Text;
using System.Text.Json;
using BuildingBlocks.Contracts;
using Notes.Contracts;

namespace Notes;

/// <summary>校验通过后可以直接落库的讲义元信息与正文。</summary>
internal sealed record NotePayload(
    string Title,
    string? Subtitle,
    List<string> Tags,
    string Visibility,
    string Year,
    string Semester,
    int SectionCount,
    string ContentJson);

/// <summary>
/// 讲义 JSON 的入库校验（格式见 .tmp/notes-format.md）。
/// 字段名统一用 content.xxx，错误一次性收集完再抛，前端能一次看到全部问题。
/// </summary>
internal static class NoteValidator
{
    private const int MaxContentBytes = 4 * 1024 * 1024;
    private const int MaxTitleLength = 120;
    private const int MaxSubtitleLength = 500;
    internal const int MaxTagCount = 30;
    internal const int MaxTagLength = 40;
    internal const int MaxYearLength = 20;
    private const int MaxSectionCount = 60;
    private const int MaxBlockDepth = 8;

    private static readonly HashSet<string> AllowedBlockTypes = new(StringComparer.Ordinal)
    {
        "card",
        "heading",
        "paragraph",
        "list",
        "table",
        "flow",
        "callout",
        "quote",
        "grid",
        "quickGroup",
        "figure",
        "summary",
        "chips",
    };

    public static NotePayload Validate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ValidationFailedException("content", "讲义内容必须是 JSON 对象");
        }

        var contentJson = JsonSerializer.Serialize(root);
        if (Encoding.UTF8.GetByteCount(contentJson) > MaxContentBytes)
        {
            throw new ValidationFailedException("content", "讲义内容不能超过 4 MB");
        }

        var errors = new ValidationErrors();

        if (!root.TryGetProperty("formatVersion", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out var version) ||
            version != 1)
        {
            errors.Add("content.formatVersion", "formatVersion 必须是 1");
        }

        var title = ReadTrimmedString(root, "title");
        if (title is null)
        {
            errors.Add("content.title", "title 不能为空");
        }
        else if (title.Length > MaxTitleLength)
        {
            errors.Add("content.title", $"title 不能超过 {MaxTitleLength} 字");
        }

        var subtitle = ReadOptionalText(root, "subtitle", MaxSubtitleLength, "content.subtitle", errors);
        var tags = ReadTags(root, errors);
        var visibility = ReadVisibility(root, errors);
        var year = ReadOptionalText(root, "year", MaxYearLength, "content.year", errors) ?? "";
        var semester = ReadSemester(root, errors);
        var sectionCount = ValidateSections(root, errors);

        if (visibility == NoteVisibility.Public && tags.Count == 0)
        {
            errors.Add("content.tags", "公开讲义至少需要一个标签");
        }

        errors.ThrowIfAny();

        return new NotePayload(title!, subtitle, tags, visibility, year, semester, sectionCount, contentJson);
    }

    private static string? ReadOptionalText(
        JsonElement root,
        string propertyName,
        int maxLength,
        string path,
        ValidationErrors errors)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(path, $"{propertyName} 必须是字符串");
            return null;
        }

        var text = element.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > maxLength)
        {
            errors.Add(path, $"{propertyName} 不能超过 {maxLength} 字");
            return null;
        }

        return text;
    }

    private static List<string> ReadTags(JsonElement root, ValidationErrors errors)
    {
        var tags = new List<string>();
        if (!root.TryGetProperty("tags", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return tags;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add("content.tags", "tags 必须是数组");
            return tags;
        }

        var items = element.EnumerateArray().ToList();
        if (items.Count > MaxTagCount)
        {
            errors.Add("content.tags", $"tags 最多 {MaxTagCount} 项");
        }

        for (var i = 0; i < items.Count; i++)
        {
            var path = $"content.tags[{i}]";
            if (items[i].ValueKind != JsonValueKind.String)
            {
                errors.Add(path, "标签必须是字符串");
                continue;
            }

            var tag = items[i].GetString()?.Trim();
            if (string.IsNullOrEmpty(tag))
            {
                errors.Add(path, "标签不能为空");
                continue;
            }

            if (tag.Length > MaxTagLength)
            {
                errors.Add(path, $"标签不能超过 {MaxTagLength} 字");
                continue;
            }

            tags.Add(tag);
        }

        return tags;
    }

    private static string ReadVisibility(JsonElement root, ValidationErrors errors)
    {
        if (!root.TryGetProperty("visibility", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return NoteVisibility.Public;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add("content.visibility", "visibility 只能是 public 或 private");
            return NoteVisibility.Public;
        }

        var visibility = element.GetString()?.Trim();
        if (visibility != NoteVisibility.Public && visibility != NoteVisibility.Private)
        {
            errors.Add("content.visibility", "visibility 只能是 public 或 private");
            return NoteVisibility.Public;
        }

        return visibility;
    }

    /// <summary>学期只能是空、上、下；缺省按空字符串处理。</summary>
    private static string ReadSemester(JsonElement root, ValidationErrors errors)
    {
        if (!root.TryGetProperty("semester", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return "";
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add("content.semester", "学期只能是上或下");
            return "";
        }

        var semester = element.GetString()?.Trim() ?? "";
        if (semester.Length > 0 && semester != "上" && semester != "下")
        {
            errors.Add("content.semester", "学期只能是上或下");
            return "";
        }

        return semester;
    }

    private static int ValidateSections(JsonElement root, ValidationErrors errors)
    {
        if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
        {
            errors.Add("content.sections", "sections 必须是数组");
            return 0;
        }

        var items = sections.EnumerateArray().ToList();
        if (items.Count < 1 || items.Count > MaxSectionCount)
        {
            errors.Add("content.sections", $"sections 需要 1–{MaxSectionCount} 项");
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var path = $"content.sections[{i}]";
            var section = items[i];
            if (section.ValueKind != JsonValueKind.Object)
            {
                errors.Add(path, "section 必须是对象");
                continue;
            }

            var id = ReadTrimmedString(section, "id");
            if (id is null)
            {
                errors.Add($"{path}.id", "section.id 不能为空");
            }
            else if (!seenIds.Add(id))
            {
                errors.Add($"{path}.id", $"section.id「{id}」重复");
            }

            if (ReadTrimmedString(section, "title") is null)
            {
                errors.Add($"{path}.title", "section.title 不能为空");
            }

            ValidateBlocks(section, $"{path}.blocks", depth: 1, errors, required: true);
        }

        return items.Count;
    }

    /// <summary>递归检查嵌套块（card.blocks / summary.blocks / grid.items[].blocks），并限制嵌套深度。</summary>
    private static void ValidateBlocks(
        JsonElement owner,
        string path,
        int depth,
        ValidationErrors errors,
        bool required)
    {
        if (!owner.TryGetProperty("blocks", out var blocks) ||
            blocks.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                errors.Add(path, "blocks 必须是数组");
            }

            return;
        }

        if (blocks.ValueKind != JsonValueKind.Array)
        {
            errors.Add(path, "blocks 必须是数组");
            return;
        }

        if (depth > MaxBlockDepth)
        {
            errors.Add(path, $"块嵌套不能超过 {MaxBlockDepth} 层");
            return;
        }

        var index = 0;
        foreach (var block in blocks.EnumerateArray())
        {
            var blockPath = $"{path}[{index}]";
            index++;

            if (block.ValueKind != JsonValueKind.Object)
            {
                errors.Add(blockPath, "块必须是对象");
                continue;
            }

            var type = ReadTrimmedString(block, "type");
            if (type is null)
            {
                errors.Add($"{blockPath}.type", "块缺少 type");
                continue;
            }

            if (!AllowedBlockTypes.Contains(type))
            {
                errors.Add($"{blockPath}.type", $"未知的块类型「{type}」");
                continue;
            }

            switch (type)
            {
                case "card":
                case "summary":
                    ValidateBlocks(block, $"{blockPath}.blocks", depth + 1, errors, required: false);
                    break;
                case "grid":
                    ValidateGridItems(block, $"{blockPath}.items", depth + 1, errors);
                    break;
            }
        }
    }

    private static void ValidateGridItems(JsonElement block, string path, int depth, ValidationErrors errors)
    {
        if (!block.TryGetProperty("items", out var items) || items.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (items.ValueKind != JsonValueKind.Array)
        {
            errors.Add(path, "items 必须是数组");
            return;
        }

        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            index++;

            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add(itemPath, "grid.item 必须是对象");
                continue;
            }

            ValidateBlocks(item, $"{itemPath}.blocks", depth, errors, required: false);
        }
    }

    private static string? ReadTrimmedString(JsonElement owner, string propertyName)
    {
        if (!owner.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private sealed class ValidationErrors
    {
        private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var messages))
            {
                messages = [];
                _errors[field] = messages;
            }

            messages.Add(message);
        }

        public void ThrowIfAny()
        {
            if (_errors.Count == 0)
            {
                return;
            }

            throw new ValidationFailedException(
                _errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal));
        }
    }
}
