using Questions.Contracts;

namespace Questions;

public sealed class StrictChoiceGrader : IQuestionGrader
{
    public GradeResult Grade(QuestionDto question, IReadOnlyCollection<string> selectedKeys)
    {
        var correct = question.Options
            .Where(o => o.Correct)
            .Select(o => o.Key)
            .ToHashSet(StringComparer.Ordinal);
        var selected = selectedKeys.ToHashSet(StringComparer.Ordinal);

        var hitCount = correct.Count(selected.Contains);
        var hasWrongSelection = selected.Any(key => !correct.Contains(key));

        var score = hasWrongSelection || correct.Count == 0
            ? 0d
            : (double)hitCount / correct.Count;

        var perOption = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in question.Options)
        {
            perOption[option.Key] = (option.Correct, selected.Contains(option.Key)) switch
            {
                (true, true) => "hit",
                (true, false) => "missed",
                (false, true) => "wrong",
                _ => "neutral",
            };
        }

        return new GradeResult(correct.SetEquals(selected), score, perOption);
    }
}
