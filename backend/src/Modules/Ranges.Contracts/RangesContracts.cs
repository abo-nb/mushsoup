namespace Ranges.Contracts;

public static class RangeMode
{
    public const string Daily = "daily";
    public const string Endless = "endless";

    public static bool IsKnown(string mode) => mode is Daily or Endless;
}

public sealed record PracticeRangeDto(
    string Id,
    string OwnerId,
    string? OwnerName,
    string BankId,
    string BankName,
    string Name,
    string? Description,
    IReadOnlyList<IReadOnlyList<string>> Chapters,
    int QuestionCount,
    int Limit,
    string Mode,
    string? Algorithm,
    bool IsMine,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateRangeRequest(
    string BankId,
    string Name,
    string? Description,
    IReadOnlyList<IReadOnlyList<string>>? Chapters,
    int QuestionCount,
    int Limit,
    string? Mode = null,
    string? Algorithm = null);
