using Microsoft.Extensions.Caching.Memory;

namespace Notes;

/// <summary>讲义缓存：key 带版本号，写操作 bump 版本即整体失效，旧 key 靠 TTL 自然淘汰。</summary>
internal sealed class NoteCache(IMemoryCache cache)
{
    private readonly object gate = new();
    private long version;

    public string Key(string suffix)
    {
        lock (gate)
        {
            return $"notes:{version}:{suffix}";
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            version++;
        }
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (cache.TryGetValue(key, out var raw))
        {
            if (raw is T typed)
            {
                value = typed;
                return true;
            }

            // 命中「值就是 null」的条目（例如查不到讲义），也算缓存命中。
            if (raw is null && default(T) is null)
            {
                value = default;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan ttl) =>
        cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
        });
}
