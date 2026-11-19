using Banks.Contracts;
using BuildingBlocks.Contracts;
using Classes.Contracts;
using Identity.Contracts;
using Leaderboard.Contracts;
using Notes.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Practice.Contracts;
using Questions.Contracts;

namespace Leaderboard;

internal sealed class LeaderboardService(
    ICurrentUser currentUser,
    IClassDirectory classes,
    IUserDirectory users,
    IQuestionCatalog questions,
    IBankDirectory banks,
    INoteDirectory notes,
    IPracticeStats practice,
    IMemoryCache cache)
{
    private const double SmoothingCorrect = 5.0;
    private const double SmoothingAnswered = 10.0;
    private const int MinAnswered = 50;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static readonly LeaderboardWeightsDto Weights = new(0.5, 0.3, 0.2);

    public async Task<LeaderboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var classId = currentUser.ClassId;
        if (string.IsNullOrWhiteSpace(classId))
        {
            classId = (await classes.GetDefaultAsync(cancellationToken)).Id;
        }

        // 拿不到班级时不缓存，避免把空结果固化 60 秒。
        if (string.IsNullOrWhiteSpace(classId))
        {
            return await BuildAsync(classId ?? string.Empty, cancellationToken);
        }

        var cacheKey = $"leaderboard:{classId}";
        if (cache.TryGetValue(cacheKey, out LeaderboardDto? cached) && cached is not null)
        {
            return cached;
        }

        var leaderboard = await BuildAsync(classId, cancellationToken);
        cache.Set(cacheKey, leaderboard, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
        });

        return leaderboard;
    }

    private async Task<LeaderboardDto> BuildAsync(string classId, CancellationToken cancellationToken)
    {
        var members = await users.ListByClassAsync(classId, cancellationToken);
        if (members.Count == 0)
        {
            return new LeaderboardDto([], Weights, DateTime.UtcNow);
        }

        var userIds = members.Select(member => member.Id).ToList();
        var questionsCreated = await questions.CountCreatedByUsersAsync(userIds, cancellationToken);
        var banksOwned = await banks.CountOwnedByUsersAsync(userIds, cancellationToken);
        var notesOwned = await notes.CountOwnedByUsersAsync(userIds, cancellationToken);
        var practiceStats = await practice.GetUserStatsAsync(userIds, cancellationToken);
        var statsByUser = practiceStats.ToDictionary(stat => stat.UserId, StringComparer.Ordinal);

        var rows = members.Select(member =>
        {
            var stats = statsByUser.GetValueOrDefault(member.Id);
            var answered = stats?.Answered ?? 0;
            var correct = stats?.Correct ?? 0;
            var created = questionsCreated.GetValueOrDefault(member.Id);
            var ownedBanks = banksOwned.GetValueOrDefault(member.Id);
            var ownedNotes = notesOwned.GetValueOrDefault(member.Id);

            return new Row(
                member.Id,
                member.DisplayName,
                member.AvatarUrl,
                correct,
                answered,
                answered == 0 ? 0 : (double)correct / answered,
                created,
                ownedBanks,
                ownedNotes,
                created + ownedBanks + ownedNotes);
        }).ToList();

        var ranked = rows.Where(row => row.Answered >= MinAnswered).ToList();
        if (ranked.Count == 0)
        {
            return new LeaderboardDto([], Weights, DateTime.UtcNow);
        }

        var maxContributions = ranked.Max(row => row.Contributions);
        var maxAnswered = ranked.Max(row => row.Answered);

        var entries = ranked
            .Select(row =>
            {
                var smoothed = row.Answered == 0
                    ? 0
                    : (row.Correct + SmoothingCorrect) / (row.Answered + SmoothingAnswered);
                var contributionNorm = maxContributions == 0
                    ? 0
                    : (double)row.Contributions / maxContributions;
                var volumeNorm = maxAnswered == 0 ? 0 : (double)row.Answered / maxAnswered;
                var weighted = Weights.Accuracy * smoothed
                               + Weights.Contributions * contributionNorm
                               + Weights.Volume * volumeNorm;

                return new ScoredRow(row, smoothed, Math.Round(weighted * 100, 1, MidpointRounding.AwayFromZero));
            })
            .OrderByDescending(scored => scored.CompositeScore)
            .ThenByDescending(scored => scored.Smoothed)
            .ThenByDescending(scored => scored.Row.Answered)
            .ThenBy(scored => scored.Row.DisplayName, StringComparer.Ordinal)
            .Select(scored => new LeaderboardEntryDto(
                scored.Row.UserId,
                scored.Row.DisplayName,
                scored.Row.AvatarUrl,
                scored.Row.Correct,
                scored.Row.Answered,
                scored.Row.Accuracy,
                scored.Row.QuestionsCreated,
                scored.Row.BanksOwned,
                scored.Row.NotesOwned,
                scored.Row.Contributions,
                scored.CompositeScore))
            .ToList();

        return new LeaderboardDto(entries, Weights, DateTime.UtcNow);
    }

    private sealed record Row(
        string UserId,
        string DisplayName,
        string? AvatarUrl,
        int Correct,
        int Answered,
        double Accuracy,
        int QuestionsCreated,
        int BanksOwned,
        int NotesOwned,
        int Contributions);

    private sealed record ScoredRow(Row Row, double Smoothed, double CompositeScore);
}
