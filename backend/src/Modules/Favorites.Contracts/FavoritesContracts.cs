namespace Favorites.Contracts;

public static class FavoriteKind
{
    public const string Bank = "bank";
    public const string Note = "note";

    public static bool IsKnown(string kind) => kind is Bank or Note;
}

/// <summary>收藏一条公开内容。CreatedAt 用于「收藏」页按时间倒序。</summary>
public sealed record FavoriteItemDto(string Kind, string ItemId, DateTime CreatedAt);

public sealed record AddFavoriteRequest(string Kind, string ItemId);
