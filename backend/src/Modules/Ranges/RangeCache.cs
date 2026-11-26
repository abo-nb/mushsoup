using Microsoft.Extensions.Caching.Memory;

namespace Ranges;

internal sealed class RangeCache(IMemoryCache cache)
{
    private readonly object gate = new();
    private long version;

    /// <summary>生成带版本号的键；<see cref="Invalidate"/> 后旧键自然失效。</summary>
    public string Key(string suffix)
    {
        lock (gate)
        {
            return $"ranges:{version}:{suffix}";
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            version++;
        }
    }

    public bool TryGet<T>(string key, out T? value) => cache.TryGetValue<T>(key, out value);

    public void Set<T>(string key, T value, TimeSpan ttl) =>
        cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
        });
}
