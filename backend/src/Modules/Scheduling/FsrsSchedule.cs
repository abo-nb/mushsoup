using Scheduling.Contracts;

namespace Scheduling;

/// <summary>
/// FSRS-5 内核，只做数学、不碰数据库。调用方负责把上次的 S/D 和距上次复习的天数传进来。
/// </summary>
/// <remarks>
/// 权重与公式出自 FSRS-5 官方规范 https://github.com/open-spaced-repetition/awesome-fsrs/wiki/The-Algorithm#fsrs-5 的 Default parameters 与 Formula 两节。
/// </remarks>
public static class FsrsSchedule
{
    /// <summary>FSRS-5 默认 19 个权重 w0..w18。</summary>
    private static readonly double[] W =
    [
        0.40255, 1.18385, 3.173, 15.69105,
        7.1949, 0.5345,
        1.4604, 0.0046,
        1.54575, 0.1192, 1.01925,
        1.9395, 0.11, 0.29605,
        2.2698,
        0.2315, 2.9898,
        0.51655, 0.6621,
    ];

    private const double Decay = -0.5;
    private const double Factor = 19.0 / 81.0;
    private const double MinStability = 0.01;
    private const double MaxStability = 36500;
    private const double MinDifficulty = 1;
    private const double MaxDifficulty = 10;
    private const int MaxIntervalDays = 36500;
    private const double MinRetention = 0.7;
    private const double MaxRetention = 0.97;
    private const double DefaultRetention = 0.9;

    /// <param name="stability">上次的稳定度（天）；新卡传 0。</param>
    /// <param name="difficulty">上次的难度（1..10）；新卡传 0。</param>
    /// <param name="rating">again / hard / good / easy，其余按 good 处理。</param>
    /// <param name="elapsedDays">距上次复习多少天；同一天内传 0，新卡传 0。</param>
    /// <param name="desiredRetention">目标保持率，例如 0.9；会夹到 0.7..0.97。</param>
    public static FsrsResult Next(
        double stability,
        double difficulty,
        string rating,
        double elapsedDays,
        double desiredRetention)
    {
        var grade = GradeOf(rating);
        var retention = RetentionOf(desiredRetention);
        var s = NonNegative(stability);
        var d = NonNegative(difficulty);
        var t = NonNegative(elapsedDays);

        if (s <= 0 || d <= 0)
        {
            var initialStability = Math.Clamp(W[grade - 1], MinStability, MaxStability);
            return new FsrsResult(
                initialStability,
                InitialDifficulty(grade),
                grade == 1 ? 0 : IntervalFor(initialStability, retention),
                grade == 1);
        }

        s = Math.Clamp(s, MinStability, MaxStability);
        d = Math.Clamp(d, MinDifficulty, MaxDifficulty);

        var nextDifficulty = NextDifficulty(d, grade);

        // 同一天内的复习走短期记忆公式，间隔归零由调用方按分钟安排。
        if (t <= 0)
        {
            var shortTerm = Math.Clamp(
                s * Math.Exp(W[17] * (grade - 3 + W[18])),
                MinStability,
                MaxStability);
            return new FsrsResult(shortTerm, nextDifficulty, 0, grade == 1);
        }

        var retrievability = Retrievability(t, s);

        if (grade == 1)
        {
            var forgotten = Math.Clamp(
                W[11] * Math.Pow(d, -W[12]) * (Math.Pow(s + 1, W[13]) - 1) * Math.Exp(W[14] * (1 - retrievability)),
                MinStability,
                MaxStability);
            return new FsrsResult(forgotten, nextDifficulty, 0, true);
        }

        var nextStability = Math.Clamp(
            s * (1
                + Math.Exp(W[8])
                * (11 - d)
                * Math.Pow(s, -W[9])
                * (Math.Exp(W[10] * (1 - retrievability)) - 1)
                * (grade == 2 ? W[15] : 1)
                * (grade == 4 ? W[16] : 1)),
            MinStability,
            MaxStability);

        return new FsrsResult(nextStability, nextDifficulty, IntervalFor(nextStability, retention), false);
    }

    /// <summary>D0(G) = clamp(w4 - e^(w5*(G-1)) + 1, 1, 10)。</summary>
    private static double InitialDifficulty(int grade) => Math.Clamp(
        W[4] - Math.Exp(W[5] * (grade - 1)) + 1,
        MinDifficulty,
        MaxDifficulty);

    /// <summary>D' = clamp(w7*D0(4) + (1-w7)*(D - w6*(G-3)*(10-D)/9), 1, 10)。</summary>
    private static double NextDifficulty(double difficulty, int grade)
    {
        var delta = -W[6] * (grade - 3);
        var damped = difficulty + delta * (10 - difficulty) / 9;
        return Math.Clamp(
            W[7] * InitialDifficulty(4) + (1 - W[7]) * damped,
            MinDifficulty,
            MaxDifficulty);
    }

    /// <summary>R(t,S) = (1 + 19/81 * t/S)^(-0.5)。</summary>
    private static double Retrievability(double elapsedDays, double stability) =>
        Math.Pow(1 + Factor * elapsedDays / stability, Decay);

    /// <summary>I(r,S) = S/(19/81) * (r^(-2) - 1)，四舍五入后夹在 1..36500 天。</summary>
    private static int IntervalFor(double stability, double retention) => (int)Math.Clamp(
        Math.Round(stability * (Math.Pow(retention, 1 / Decay) - 1) / Factor, MidpointRounding.AwayFromZero),
        1,
        MaxIntervalDays);

    private static int GradeOf(string rating) => rating switch
    {
        ReviewRating.Again => 1,
        ReviewRating.Hard => 2,
        ReviewRating.Easy => 4,
        _ => 3,
    };

    private static double RetentionOf(double desiredRetention) => double.IsFinite(desiredRetention)
        ? Math.Clamp(desiredRetention, MinRetention, MaxRetention)
        : DefaultRetention;

    private static double NonNegative(double value) => double.IsFinite(value) ? Math.Max(value, 0) : 0;
}

/// <summary><see cref="FsrsResult.IntervalDays"/> 为 0 表示当天重来（按分钟安排）。</summary>
public readonly record struct FsrsResult(
    double Stability,
    double Difficulty,
    int IntervalDays,
    bool Lapsed);
