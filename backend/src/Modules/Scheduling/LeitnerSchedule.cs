using Scheduling.Contracts;

namespace Scheduling;

/// <summary>
/// Leitner 盒子：答对进下一格，答错回第一格。间隔是固定的 1/2/4/8/16/32 天，
/// 没有任何可调参数，方便直接跟人解释「这题现在在第几格」。
/// </summary>
public static class LeitnerSchedule
{
    /// <summary>第 6 格（32 天）就是最后一格。</summary>
    public const int MaxBox = 6;

    /// <summary>每格对应的间隔天数，0 号格表示还在学习、当天重来。</summary>
    public static readonly int[] Intervals = [0, 1, 2, 4, 8, 16, 32];

    /// <param name="box">当前在第几格，0 表示新卡。</param>
    /// <param name="rating">again / hard / good / easy，只有 again 算答错。</param>
    public static LeitnerResult Next(int box, string rating)
    {
        var current = Math.Clamp(box, 0, MaxBox);

        if (rating == ReviewRating.Again)
        {
            return new LeitnerResult(0, Intervals[0], true);
        }

        var next = Math.Min(current + 1, MaxBox);
        return new LeitnerResult(next, Intervals[next], false);
    }
}

/// <summary><see cref="LeitnerResult.IntervalDays"/> 为 0 表示当天重来（按分钟安排）。</summary>
public readonly record struct LeitnerResult(int Box, int IntervalDays, bool Lapsed);
