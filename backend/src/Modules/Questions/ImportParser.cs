using System.Text.Json;
using Questions.Contracts;

namespace Questions;

internal sealed record ParsedImport(
    List<JsonElement> Questions,
    int FormatVersion,
    List<ImportIssueDto> Issues);

/// <summary>
/// LLM 的输出很少是干净的 JSON：会包代码围栏、加说明文字、留尾逗号。
/// 这里按「原样 → 去围栏 → 截取平衡块 → 允许尾逗号与注释」的顺序放宽，全都失败才报语法错误。
/// </summary>
internal static class ImportParser
{
    public static ParsedImport Parse(string payload)
    {
        var issues = new List<ImportIssueDto>();
        var raw = payload?.Trim() ?? "";
        if (raw.Length == 0)
        {
            issues.Add(new ImportIssueDto(ImportIssueCodes.JsonSyntax, "内容为空", ImportSeverity.Error));
            return new ParsedImport([], 1, issues);
        }

        var attempts = new List<string> { raw };
        var stripped = StripFence(raw);
        if (stripped != raw)
        {
            attempts.Add(stripped);
        }

        var extracted = ExtractJson(stripped);
        if (extracted is not null && extracted != stripped)
        {
            attempts.Add(extracted);
        }

        JsonDocument? document = null;
        string? lastError = null;
        foreach (var attempt in attempts)
        {
            try
            {
                document = JsonDocument.Parse(attempt, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                break;
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
            }
        }

        if (document is null)
        {
            issues.Add(new ImportIssueDto(
                ImportIssueCodes.JsonSyntax,
                $"无法解析 JSON，请检查是否完整复制了模型输出（{lastError ?? "格式不正确"}）",
                ImportSeverity.Error));
            return new ParsedImport([], 1, issues);
        }

        using (document)
        {
            var root = document.RootElement;
            var version = 1;
            List<JsonElement> questions;

            if (root.ValueKind == JsonValueKind.Array)
            {
                questions = root.EnumerateArray().Select(element => element.Clone()).ToList();
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("formatVersion", out var versionElement) &&
                    versionElement.ValueKind == JsonValueKind.Number &&
                    versionElement.TryGetInt32(out var parsedVersion))
                {
                    version = parsedVersion;
                }

                if (!root.TryGetProperty("questions", out var questionElements) ||
                    questionElements.ValueKind != JsonValueKind.Array)
                {
                    issues.Add(new ImportIssueDto(
                        ImportIssueCodes.Schema,
                        "缺少 questions 数组",
                        ImportSeverity.Error));
                    return new ParsedImport([], version, issues);
                }

                questions = questionElements.EnumerateArray().Select(element => element.Clone()).ToList();
            }
            else
            {
                issues.Add(new ImportIssueDto(
                    ImportIssueCodes.Schema,
                    "顶层必须是对象或数组",
                    ImportSeverity.Error));
                return new ParsedImport([], version, issues);
            }

            if (version != 1)
            {
                issues.Add(new ImportIssueDto(
                    ImportIssueCodes.FormatVersion,
                    $"formatVersion 是 {version}，当前按 1 解析",
                    ImportSeverity.Warning));
            }

            return new ParsedImport(questions, version, issues);
        }
    }

    private static string StripFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return trimmed;
        }

        var body = trimmed[(firstLineEnd + 1)..];
        var closing = body.LastIndexOf("```", StringComparison.Ordinal);
        if (closing >= 0)
        {
            body = body[..closing];
        }

        return body.Trim();
    }

    private static string? ExtractJson(string text)
    {
        var objectStart = text.IndexOf('{');
        var arrayStart = text.IndexOf('[');

        var useObject = objectStart >= 0 && (arrayStart < 0 || objectStart < arrayStart);
        if (useObject)
        {
            var end = text.LastIndexOf('}');
            return end > objectStart ? text[objectStart..(end + 1)] : null;
        }

        if (arrayStart >= 0)
        {
            var end = text.LastIndexOf(']');
            return end > arrayStart ? text[arrayStart..(end + 1)] : null;
        }

        return null;
    }
}
