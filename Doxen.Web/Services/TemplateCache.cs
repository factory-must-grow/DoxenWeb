using Microsoft.Extensions.Caching.Memory;

namespace Doxen.Web.Services;

public sealed record UploadedTemplate(byte[] Bytes, string FileName);

public enum TemplateAdmitResult
{
    Admitted,
    Rejected,
}

// Настройки бюджета памяти под загруженные шаблоны — см. 01-architecture.md,
// раздел «Бюджет памяти и допуск при нехватке».
public sealed class TemplateCacheOptions
{
    public long BudgetBytes { get; set; } = 2_000_000_000;
    public int SoftLimitPercent { get; set; } = 75;
    public int HardLimitPercent { get; set; } = 90;
    public int FreeUserWaitSeconds { get; set; } = 20;
    public int MaxQueuedFreeUsers { get; set; } = 50;
    public int LifetimeHours { get; set; } = 2;
}

// Загруженный шаблон живёт только в оперативной памяти, никогда на
// диске — см. 00-README.md и 01-architecture.md. В сессии пользователя
// хранится только ключ записи, не сам файл.
public sealed class TemplateCache
{
    private readonly IMemoryCache _cache;
    private readonly TemplateCacheOptions _options;
    private long _usedBytes;
    private int _waitingCount;

    // Пульс для бесплатных пользователей, ждущих освобождения места —
    // отпускается при вытеснении записи из кеша.
    private readonly SemaphoreSlim _freedSignal = new(0);

    public TemplateCache(IMemoryCache cache, TemplateCacheOptions options)
    {
        _cache = cache;
        _options = options;
    }

    public long UsedBytes => Interlocked.Read(ref _usedBytes);
    public long BudgetBytes => _options.BudgetBytes;
    public double UsedPercent => BudgetBytes <= 0 ? 0 : 100.0 * UsedBytes / BudgetBytes;

    public async Task<TemplateAdmitResult> TryAdmitAsync(bool isPaidPlan, CancellationToken ct)
    {
        var percent = UsedPercent;

        if (percent < _options.SoftLimitPercent)
        {
            return TemplateAdmitResult.Admitted;
        }

        if (isPaidPlan)
        {
            // Платные проходят сразу вплоть до жёсткого порога.
            return percent < _options.HardLimitPercent ? TemplateAdmitResult.Admitted : TemplateAdmitResult.Rejected;
        }

        if (percent >= _options.HardLimitPercent)
        {
            return TemplateAdmitResult.Rejected;
        }

        // 75–90%, бесплатный пользователь: ждём освобождения места, но не
        // дольше настроенного времени и не больше настроенного числа
        // одновременно ждущих — иначе очередь сама станет причиной отказа.
        if (Interlocked.Increment(ref _waitingCount) > _options.MaxQueuedFreeUsers)
        {
            Interlocked.Decrement(ref _waitingCount);
            return TemplateAdmitResult.Rejected;
        }

        try
        {
            var freed = await _freedSignal.WaitAsync(TimeSpan.FromSeconds(_options.FreeUserWaitSeconds), ct);
            return freed && UsedPercent < _options.HardLimitPercent
                ? TemplateAdmitResult.Admitted
                : TemplateAdmitResult.Rejected;
        }
        finally
        {
            Interlocked.Decrement(ref _waitingCount);
        }
    }

    public string Store(byte[] bytes, string fileName)
    {
        var key = "tpl:" + Guid.NewGuid().ToString("N");
        var options = new MemoryCacheEntryOptions
        {
            Size = bytes.Length,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_options.LifetimeHours),
            SlidingExpiration = TimeSpan.FromMinutes(30),
        };

        options.RegisterPostEvictionCallback((_, _, _, _) =>
        {
            Interlocked.Add(ref _usedBytes, -bytes.Length);
            var waiting = Math.Max(1, Volatile.Read(ref _waitingCount));
            _freedSignal.Release(waiting);
        });

        Interlocked.Add(ref _usedBytes, bytes.Length);
        _cache.Set(key, new UploadedTemplate(bytes, fileName), options);
        return key;
    }

    public UploadedTemplate? Get(string key) => _cache.Get<UploadedTemplate>(key);
}
