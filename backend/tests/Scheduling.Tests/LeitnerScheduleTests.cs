using Scheduling;
using Scheduling.Contracts;

namespace Scheduling.Tests;

public class LeitnerScheduleTests
{
    [Fact]
    public void Correct_answers_walk_from_box_zero_to_top()
    {
        var expected = new[] { (1, 1), (2, 2), (3, 4), (4, 8), (5, 16), (6, 32) };
        var box = 0;

        foreach (var (nextBox, interval) in expected)
        {
            var result = LeitnerSchedule.Next(box, ReviewRating.Good);

            Assert.Equal(nextBox, result.Box);
            Assert.Equal(interval, result.IntervalDays);
            Assert.False(result.Lapsed);
            box = result.Box;
        }
    }

    [Theory]
    [InlineData(ReviewRating.Hard)]
    [InlineData(ReviewRating.Good)]
    [InlineData(ReviewRating.Easy)]
    public void Any_non_again_rating_advances_one_box(string rating)
    {
        var result = LeitnerSchedule.Next(2, rating);

        Assert.Equal(3, result.Box);
        Assert.Equal(4, result.IntervalDays);
        Assert.False(result.Lapsed);
    }

    [Fact]
    public void Top_box_does_not_grow()
    {
        var result = LeitnerSchedule.Next(LeitnerSchedule.MaxBox, ReviewRating.Easy);

        Assert.Equal(LeitnerSchedule.MaxBox, result.Box);
        Assert.Equal(32, result.IntervalDays);
        Assert.False(result.Lapsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void Again_returns_to_box_zero(int box)
    {
        var result = LeitnerSchedule.Next(box, ReviewRating.Again);

        Assert.Equal(0, result.Box);
        Assert.Equal(0, result.IntervalDays);
        Assert.True(result.Lapsed);
    }

    [Fact]
    public void Unknown_rating_is_treated_as_correct()
    {
        var result = LeitnerSchedule.Next(1, "unspecified");

        Assert.Equal(2, result.Box);
        Assert.Equal(2, result.IntervalDays);
        Assert.False(result.Lapsed);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(7, 6)]
    [InlineData(int.MaxValue, 6)]
    public void Out_of_range_box_is_clamped_before_advancing(int box, int expectedBox)
    {
        var result = LeitnerSchedule.Next(box, ReviewRating.Good);

        Assert.Equal(expectedBox, result.Box);
        Assert.Equal(LeitnerSchedule.Intervals[expectedBox], result.IntervalDays);
        Assert.False(result.Lapsed);
    }
}
