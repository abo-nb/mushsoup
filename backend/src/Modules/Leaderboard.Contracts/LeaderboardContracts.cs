namespace Leaderboard.Contracts;

public sealed record LeaderboardWeightsDto(double Accuracy, double Contributions, double Volume);

/// <summary>Accuracy 是原始答对率（0–1）；Contributions 是题目、题库、讲义三项之和。</summary>
public sealed record LeaderboardEntryDto(
    string UserId,
    string DisplayName,
    string? AvatarUrl,
    int Correct,
    int Answered,
    double Accuracy,
    int QuestionsCreated,
    int BanksOwned,
    int NotesOwned,
    int Contributions,
    double CompositeScore);

public sealed record LeaderboardDto(
    IReadOnlyList<LeaderboardEntryDto> Entries,
    LeaderboardWeightsDto Weights,
    DateTime GeneratedAt);
