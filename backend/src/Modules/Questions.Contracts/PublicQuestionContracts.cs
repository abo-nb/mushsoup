namespace Questions.Contracts;

/// <summary>游客作答只用于即时判分，不记录任何进度。</summary>
public sealed record GuestAnswerRequest(IReadOnlyList<string>? SelectedKeys);

public sealed record GuestAnswerResultDto(
    bool Correct,
    IReadOnlyList<string> CorrectKeys,
    string Stem,
    string Explanation);
