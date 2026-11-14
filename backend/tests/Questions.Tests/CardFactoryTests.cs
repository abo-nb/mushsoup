using Questions;
using Questions.Contracts;
using Xunit;

namespace Questions.Tests;

public class CardFactoryTests
{
    private static readonly (string Key, string Text)[] Options =
    [
        ("A", "甲"),
        ("B", "乙"),
        ("C", "丙"),
        ("D", "丁"),
    ];

    private static CardDto Build(int attemptNo, bool shuffle = true, string userId = "u1") =>
        CardAssembler.Assemble("q1", QuestionTypes.SingleChoice, "题干", Options, userId, attemptNo, shuffle);

    [Fact]
    public void Same_user_and_attempt_yield_identical_order()
    {
        var first = Build(3);
        var second = Build(3);

        Assert.Equal(first.Options.Select(o => o.Key), second.Options.Select(o => o.Key));
        Assert.Equal(first.Seed, second.Seed);
    }

    [Fact]
    public void Shuffle_disabled_keeps_author_order()
    {
        var card = Build(0, shuffle: false);

        Assert.Equal(["A", "B", "C", "D"], card.Options.Select(o => o.Key));
    }

    [Fact]
    public void Labels_are_positional()
    {
        var card = Build(1);

        Assert.Equal("ABCD", string.Concat(card.Options.Select(o => o.Label)));
        Assert.Equal(4, card.Options.Count);
        Assert.Distinct(card.Options.Select(o => o.Key));
    }

    [Fact]
    public void Order_changes_across_attempts()
    {
        var orders = Enumerable.Range(0, 10)
            .Select(attempt => string.Join(",", Build(attempt).Options.Select(o => o.Key)))
            .Distinct()
            .Count();

        Assert.True(orders >= 2, "十次尝试至少应出现两种顺序");
    }

    [Fact]
    public void Different_users_get_different_orders()
    {
        var orders = new[] { "u1", "u2", "u3" }
            .Select(user => string.Join(",", Build(0, userId: user).Options.Select(o => o.Key)))
            .Distinct()
            .Count();

        Assert.True(orders >= 2);
    }

    [Fact]
    public void Pool_sampling_is_deterministic_and_distinct()
    {
        var candidates = Enumerable.Range(0, 20).Select(i => $"entry-{i}").ToArray();
        var seed = CardAssembler.DeriveSeed("u1", "q1", 5);

        var first = CardAssembler.Sample(candidates, 3, seed);
        var second = CardAssembler.Sample(candidates, 3, seed);

        Assert.Equal(first, second);
        Assert.Equal(3, first.Count);
        Assert.Equal(3, first.Distinct().Count());
    }

    [Fact]
    public void Pool_sampling_shrinks_when_pool_is_small()
    {
        var sampled = CardAssembler.Sample(new[] { "a", "b" }, 5, 42UL);

        Assert.Equal(2, sampled.Count);
    }
}
