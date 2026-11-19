namespace BuildingBlocks.Contracts;

/// <summary>本地统计的每日数据库写入额度已用完，由 Host 转成 429。</summary>
public sealed class QuotaExceededException(string message) : AppException("usage_limit", message, 429);
