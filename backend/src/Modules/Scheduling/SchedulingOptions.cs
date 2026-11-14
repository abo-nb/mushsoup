namespace Scheduling;

public sealed class SchedulingOptions
{
    /// <summary>间隔达到这个天数才算「已掌握」。</summary>
    public int MasteredIntervalDays { get; set; } = 21;

    public int MasteredReps { get; set; } = 3;

    public double InitialEase { get; set; } = 2.5;

    /// <summary>答错后多久重新出现（分钟）。只影响跨会话的复习队列。</summary>
    public int RelearnMinutes { get; set; } = 10;

    public double TargetRetention { get; set; } = 0.9;
}
