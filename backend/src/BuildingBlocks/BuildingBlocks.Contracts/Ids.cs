namespace BuildingBlocks.Contracts;

public static class Ids
{
    /// <summary>时间有序的标识，按创建顺序自然排序。</summary>
    public static string New() => Guid.CreateVersion7().ToString("N");
}
