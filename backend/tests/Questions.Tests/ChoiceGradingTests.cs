using Questions;
using Questions.Contracts;
using Xunit;

namespace Questions.Tests;

public class ChoiceGradingTests
{
    private static QuestionDto Single() => new(
        "q1", "b1", QuestionTypes.SingleChoice, "题干",
        [
            new QuestionOptionDto("A", "甲", false),
            new QuestionOptionDto("B", "乙", true),
            new QuestionOptionDto("C", "丙", false),
            new QuestionOptionDto("D", "丁", false),
        ],
        ShuffleMode.Follow, ["tag"], 2, null, "解析", QuestionStatus.Published, true, 1,
        "u1", DateTime.UtcNow, DateTime.UtcNow, []);

    private static QuestionDto Multiple() => new(
        "q2", "b1", QuestionTypes.MultipleChoice, "题干",
        [
            new QuestionOptionDto("A", "甲", true),
            new QuestionOptionDto("B", "乙", true),
            new QuestionOptionDto("C", "丙", false),
            new QuestionOptionDto("D", "丁", true),
        ],
        ShuffleMode.Follow, [], 2, null, "", QuestionStatus.Published, true, 1,
        "u1", DateTime.UtcNow, DateTime.UtcNow, []);

    [Fact]
    public void Single_choice_hit_is_correct()
    {
        var result = new StrictChoiceGrader().Grade(Single(), ["B"]);

        Assert.True(result.Correct);
        Assert.Equal(1d, result.Score);
        Assert.Equal("hit", result.PerOption["B"]);
        Assert.Equal("neutral", result.PerOption["A"]);
    }

    [Fact]
    public void Single_choice_wrong_selection_marks_wrong_and_missed()
    {
        var result = new StrictChoiceGrader().Grade(Single(), ["A"]);

        Assert.False(result.Correct);
        Assert.Equal(0d, result.Score);
        Assert.Equal("wrong", result.PerOption["A"]);
        Assert.Equal("missed", result.PerOption["B"]);
    }

    [Fact]
    public void Multiple_choice_requires_exact_set()
    {
        var grader = new StrictChoiceGrader();

        var partial = grader.Grade(Multiple(), ["A", "B"]);
        Assert.False(partial.Correct);
        Assert.Equal(2d / 3d, partial.Score, 3);
        Assert.Equal("missed", partial.PerOption["D"]);

        var exact = grader.Grade(Multiple(), ["A", "B", "D"]);
        Assert.True(exact.Correct);
        Assert.Equal(1d, exact.Score);
    }

    [Fact]
    public void Multiple_choice_extra_selection_zeroes_the_score()
    {
        var result = new StrictChoiceGrader().Grade(Multiple(), ["A", "B", "C", "D"]);

        Assert.False(result.Correct);
        Assert.Equal(0d, result.Score);
        Assert.Equal("wrong", result.PerOption["C"]);
    }
}
