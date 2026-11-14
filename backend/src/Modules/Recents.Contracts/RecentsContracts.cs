namespace Recents.Contracts;

public static class RecentKind
{
    public const string Bank = "bank";
    public const string Note = "note";
}

/// <summary>打开某个题库或讲义时上报，服务端按用户去重累计。</summary>
public sealed record RecordRecentRequest(string Kind, string ItemId);

public sealed record RecentItemDto(string Kind, string ItemId, DateTime LastOpenedAt, int OpenCount);
