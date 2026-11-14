using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Questions.Contracts;

namespace Questions;

/// <summary>
/// 组卡的全部纯逻辑：确定性洗牌、池选项抽样、标签生成。
/// 独立出来是为了让「同一 (用户, 题, 第几次) 必得同一张卡」可以被单测钉死。
/// </summary>
public static class CardAssembler
{
    public static ulong DeriveSeed(string userId, string questionId, int attemptNo)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}|{questionId}|{attemptNo}"));
        return BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, 8));
    }

    public static CardDto Assemble(
        string questionId,
        string type,
        string stem,
        IReadOnlyList<(string Key, string Text)> options,
        string userId,
        int attemptNo,
        bool shuffle)
    {
        var seed = DeriveSeed(userId, questionId, attemptNo);
        var state = (uint)(seed ^ (seed >> 32));
        var list = options.ToList();

        if (shuffle)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = (int)(Next(ref state) % (uint)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        var cards = list
            .Select((option, index) => new CardOptionDto(option.Key, Label(index), option.Text))
            .ToList();

        return new CardDto(questionId, type, stem, cards, seed.ToString("x16"));
    }

    /// <summary>确定性抽取 count 个元素。同一个 seed 永远抽到同一组。</summary>
    public static IReadOnlyList<T> Sample<T>(IReadOnlyList<T> candidates, int count, ulong seed)
    {
        if (count <= 0 || candidates.Count == 0)
        {
            return [];
        }

        var state = (uint)(seed ^ (seed >> 32) ^ 0x5BF03635);
        var copy = candidates.ToList();
        var take = Math.Min(count, copy.Count);

        for (var i = 0; i < take; i++)
        {
            var j = i + (int)(Next(ref state) % (uint)(copy.Count - i));
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy.Take(take).ToList();
    }

    internal static uint Next(ref uint state)
    {
        // xorshift32 的固定点只有 0，用奇数常数替换避免陷入全零序列。
        if (state == 0)
        {
            state = 0x9E3779B9;
        }

        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static string Label(int index)
    {
        var label = string.Empty;
        var value = index;
        do
        {
            label = (char)('A' + value % 26) + label;
            value = value / 26 - 1;
        }
        while (value >= 0);

        return label;
    }
}

internal sealed class DeterministicCardFactory(QuestionPoolStore pools) : ICardFactory
{
    public async Task<CardDto> BuildAsync(
        QuestionDto question,
        string userId,
        int attemptNo,
        bool shuffle,
        CancellationToken cancellationToken = default)
    {
        var options = question.Options
            .Select(option => (option.Key, option.Text))
            .ToList();

        if (question.DistractorRule is { Count: > 0 } rule)
        {
            var entries = await pools.GetEntriesAsync(rule.PoolId, includeDisabled: false, cancellationToken);
            var used = options
                .Select(option => QuestionNormalizer.Normalize(option.Text))
                .ToHashSet(StringComparer.Ordinal);

            var candidates = new List<(string Key, string Text)>();
            foreach (var entry in entries)
            {
                if (used.Add(QuestionNormalizer.Normalize(entry.Text)))
                {
                    candidates.Add((entry.Id, entry.Text));
                }
            }

            var seed = CardAssembler.DeriveSeed(userId, question.Id, attemptNo);
            foreach (var picked in CardAssembler.Sample(candidates, rule.Count, seed))
            {
                options.Add(picked);
            }
        }

        return CardAssembler.Assemble(
            question.Id,
            question.Type,
            question.Stem,
            options,
            userId,
            attemptNo,
            shuffle);
    }
}
