namespace Scheduling;

public sealed class SchedulingOptions
{
    /// <summary>间隔达到这个天数才算「已掌握」。</summary>
    public int MasteredIntervalDays { get; set; } = 21;

    public int MasteredReps { get; set; } = 3;

    public double InitialEase { get; set; } = 2.5;

    /// <summary>答错后多久重新出现（分钟）。只影响跨会话的复习队列。</summary>
    public int RelearnMinutes { get; set; } = 10;

    /// <summary>SM-2 的学习步（分钟）。新卡和重学卡要依次走完才毕业到按天计算。</summary>
    public int[] LearningSteps { get; set; } = [1, 10];

    /// <summary>FSRS 下新卡第一次答对后的间隔上限（天），避免一上来就排到几天后。</summary>
    public int NewCardIntervalDays { get; set; } = 1;

    /// <summary>FSRS 的目标保持率。</summary>
    public double TargetRetention { get; set; } = 0.9;
}
