using System.Security.Cryptography;
using System.Text;

namespace Questions;

public static class QuestionNormalizer
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var raw in value)
        {
            var ch = ToHalfWidth(raw);
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    public static string ComputeContentHash(string stem, IEnumerable<string> optionTexts)
    {
        ArgumentNullException.ThrowIfNull(optionTexts);

        var normalizedOptions = optionTexts
            .Select(Normalize)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var payload = Normalize(stem) + "\n" + string.Join("\n", normalizedOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static char ToHalfWidth(char ch) => ch switch
    {
        '\u3000' => ' ',
        >= '\uFF01' and <= '\uFF5E' => (char)(ch - 0xFEE0),
        _ => ch,
    };
}
