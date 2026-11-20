namespace Scheduling.Tests;

public class FsrsScheduleTests
{
    private const double Retention = 0.9;

    private static readonly string[] PassingRatings = ["hard", "good", "easy"];

    [Fact]
    public void New_card_initial_stability_grows_with_rating()
    {
        var again = FsrsSchedule.Next(0, 0, "again", 0, Retention);
        var hard = FsrsSchedule.Next(0, 0, "hard", 0, Retention);
        var good = FsrsSchedule.Next(0, 0, "good", 0, Retention);
        var easy = FsrsSchedule.Next(0, 0, "easy", 0, Retention);

        Assert.True(again.Stability > 0);
        Assert.True(again.Stability < hard.Stability);
        Assert.True(hard.Stability < good.Stability);
        Assert.True(good.Stability < easy.Stability);
    }

    [Fact]
    public void New_card_passing_rating_schedules_positive_interval()
    {
        foreach (var rating in PassingRatings)
        {
            var result = FsrsSchedule.Next(0, 0, rating, 0, Retention);

            Assert.False(result.Lapsed);
            Assert.InRange(result.IntervalDays, 1, 36500);
        }
    }

    [Fact]
    public void Again_is_a_lapse_without_interval()
    {
        var result = FsrsSchedule.Next(10, 5, "again", 10, Retention);

        Assert.True(result.Lapsed);
        Assert.Equal(0, result.IntervalDays);
        Assert.True(result.Stability < 10);
        Assert.InRange(result.Stability, 0.01, 36500);
    }

    [Fact]
    public void Interval_at_ninety_percent_retention_tracks_stability()
    {
        var result = FsrsSchedule.Next(10, 5, "good", 10, Retention);

        Assert.False(result.Lapsed);
        Assert.True(Math.Abs(result.IntervalDays - result.Stability) <= 1);
    }

    [Fact]
    public void Consecutive_successes_increase_stability()
    {
        var state = FsrsSchedule.Next(0, 0, "good", 0, Retention);

        for (var i = 0; i < 5; i++)
        {
            var next = FsrsSchedule.Next(state.Stability, state.Difficulty, "good", state.IntervalDays, Retention);

            Assert.True(next.Stability > state.Stability);
            state = next;
        }
    }

    [Fact]
    public void Same_day_review_keeps_zero_interval_and_updates_stability()
    {
        var result = FsrsSchedule.Next(5, 5, "good", 0, Retention);

        Assert.Equal(0, result.IntervalDays);
        Assert.False(result.Lapsed);
        Assert.NotEqual(5, result.Stability);
        Assert.True(result.Stability > 0);
    }

    [Fact]
    public void Same_day_again_is_still_a_lapse()
    {
        var result = FsrsSchedule.Next(10, 5, "again", 0, Retention);

        Assert.True(result.Lapsed);
        Assert.Equal(0, result.IntervalDays);
        Assert.True(result.Stability < 10);
    }

    [Fact]
    public void Unknown_rating_is_treated_as_good()
    {
        var unknown = FsrsSchedule.Next(0, 0, "nope", 0, Retention);
        var good = FsrsSchedule.Next(0, 0, "good", 0, Retention);

        Assert.Equal(good, unknown);
    }

    [Fact]
    public void Retention_is_clamped_to_supported_range()
    {
        var zero = FsrsSchedule.Next(10, 5, "good", 10, 0);
        var relaxed = FsrsSchedule.Next(10, 5, "good", 10, 0.7);
        var strict = FsrsSchedule.Next(10, 5, "good", 10, 0.97);

        Assert.Equal(relaxed.IntervalDays, zero.IntervalDays);
        Assert.True(relaxed.IntervalDays > strict.IntervalDays);
    }

    [Fact]
    public void Extreme_inputs_stay_within_bounds()
    {
        var cases = new (double Stability, double Difficulty, string Rating, double Elapsed, double Retention)[]
        {
            (0, 0, "again", 0, 0),
            (-10, -3, "good", -5, 0),
            (double.NaN, double.NaN, "", double.NaN, double.NaN),
            (double.PositiveInfinity, double.PositiveInfinity, "easy", double.PositiveInfinity, 5),
            (4e9, 99, "hard", 1e9, -1),
            (1e-9, 1e-9, "good", 0, 2),
        };

        foreach (var item in cases)
        {
            var result = FsrsSchedule.Next(
                item.Stability,
                item.Difficulty,
                item.Rating,
                item.Elapsed,
                item.Retention);

            Assert.True(double.IsFinite(result.Stability));
            Assert.True(double.IsFinite(result.Difficulty));
            Assert.InRange(result.Stability, 0.01, 36500);
            Assert.InRange(result.Difficulty, 1, 10);
            Assert.InRange(result.IntervalDays, 0, 36500);
        }
    }
}
