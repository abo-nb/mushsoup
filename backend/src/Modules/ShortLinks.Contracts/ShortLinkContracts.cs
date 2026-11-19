namespace ShortLinks.Contracts;

public static class ShareKind
{
    public const string Bank = "bank";
    public const string Note = "note";

    public static bool IsKnown(string kind) => kind is Bank or Note;
}

public sealed record CreateShortLinkRequest(string Kind, string TargetId);

public sealed record ShortLinkDto(string Code, string Kind, string TargetId);
