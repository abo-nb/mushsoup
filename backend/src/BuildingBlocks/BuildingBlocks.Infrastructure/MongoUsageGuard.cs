using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Contracts;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver.Core.Events;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// 在应用层按「文档数」统计 MongoDB 每日写入量，防止把上游免费额度用穿。
/// 超限时抛 <see cref="QuotaExceededException"/>，由 Host 转成 429。
/// </summary>
public sealed class MongoUsageGuard : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object gate = new();
    private readonly object flushGate = new();
    private readonly MongoUsageLimits limits;
    private readonly ILogger<MongoUsageGuard> logger;
    private readonly string statePath;
    private readonly Timer flushTimer;

    private DateOnly day = Today;
    private long writes;
    private bool dirty;

    public MongoUsageGuard(MongoOptions options, ILogger<MongoUsageGuard> logger)
    {
        limits = options.UsageLimits ?? new MongoUsageLimits();
        this.logger = logger;
        statePath = Path.GetFullPath(limits.StateFile);

        LoadState();

        flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string DayKey(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void EnsureWrite()
    {
        if (!limits.Enabled)
        {
            return;
        }

        lock (gate)
        {
            Rollover();
            if (writes >= limits.DailyWrites)
            {
                throw new QuotaExceededException($"今天的数据库写入额度已用完（{limits.DailyWrites} 次），请明天再试");
            }
        }
    }

    public void RecordCommand(CommandSucceededEvent e)
    {
        if (!limits.Enabled)
        {
            return;
        }

        try
        {
            long writeDelta = 0;

            switch (e.CommandName)
            {
                case "insert":
                    writeDelta = GetNumber(e.Reply, "n");
                    break;
                case "update":
                    writeDelta = e.Reply.Contains("n")
                        ? GetNumber(e.Reply, "n")
                        : GetNumber(e.Reply, "nModified");
                    break;
                case "delete":
                    writeDelta = GetNumber(e.Reply, "n");
                    break;
                case "findAndModify":
                    if (e.Reply.TryGetValue("value", out var value) && !value.IsBsonNull)
                    {
                        writeDelta = 1;
                    }

                    break;
                default:
                    return;
            }

            if (writeDelta == 0)
            {
                return;
            }

            lock (gate)
            {
                Rollover();
                writes += writeDelta;
                dirty = true;
            }
        }
        catch
        {
            // 统计失败不能影响真实请求。
        }
    }

    public void Flush()
    {
        UsageState state;
        lock (gate)
        {
            if (!dirty)
            {
                return;
            }

            state = new UsageState
            {
                Day = DayKey(day),
                Writes = writes,
            };
            dirty = false;
        }

        lock (flushGate)
        {
            try
            {
                var directory = Path.GetDirectoryName(statePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = statePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
                File.Move(tempPath, statePath, true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Mongo 用量计数写入 {Path} 失败", statePath);
                lock (gate)
                {
                    dirty = true;
                }
            }
        }
    }

    public void Dispose()
    {
        flushTimer.Dispose();
        Flush();
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<UsageState>(File.ReadAllText(statePath), JsonOptions);
            if (state is null || state.Day != DayKey(day))
            {
                return;
            }

            writes = Math.Max(0, state.Writes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mongo 用量状态 {Path} 读取失败，今天的计数从 0 开始", statePath);
        }
    }

    private void Rollover()
    {
        var today = Today;
        if (day == today)
        {
            return;
        }

        day = today;
        writes = 0;
        dirty = true;
    }

    private static long GetNumber(BsonDocument reply, string name) =>
        reply.TryGetValue(name, out var value) && value.IsNumeric ? value.ToInt64() : 0;

    private sealed class UsageState
    {
        public string? Day { get; set; }

        public long Writes { get; set; }
    }
}
