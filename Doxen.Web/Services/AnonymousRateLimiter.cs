using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;

namespace Doxen.Web.Services;

// Частота анонимного разбора шаблона на главной — не чаще 10 раз в
// минуту с одного IP, простой счётчик в IMemoryCache, без БД
// (05-screens.md).
public sealed class AnonymousRateLimiter
{
    private const int MaxPerMinute = 10;
    private readonly IMemoryCache _cache;

    public AnonymousRateLimiter(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryRegister(string ip)
    {
        var counter = _cache.GetOrCreate("rate:" + ip, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            entry.Size = 1; // тот же IMemoryCache, что и TemplateCache, — у него задан SizeLimit
            return new StrongBox<int>(0);
        })!;

        return Interlocked.Increment(ref counter.Value) <= MaxPerMinute;
    }
}
