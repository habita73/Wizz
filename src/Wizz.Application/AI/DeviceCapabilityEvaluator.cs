namespace Wizz.Application.AI;

using Wizz.Application.Abstractions;

/// <summary>
/// مقيّم قدرة الجهاز على تشغيل نموذج ذكاء اصطناعي محلي.
/// يبني على الموارد القابلة للقياس (RAM، توفر Ollama، توفر الإنترنت).
/// </summary>
public sealed class DeviceCapabilityEvaluator : IDeviceCapabilityEvaluator
{
    /// <summary>الحد الأدنى من RAM لتشغيل نموذج محلي خفيف (2 غيغابايت).</summary>
    private const long MinRamForLocalModel = 2L * 1024 * 1024 * 1024;

    /// <summary>الحد الأدنى من RAM لنموذج متوسط (8 غيغابايت).</summary>
    private const long MinRamForMediumModel = 8L * 1024 * 1024 * 1024;

    /// <summary>الحد الأدنى من RAM لنموذج ثقيل (16 غيغابايت).</summary>
    private const long MinRamForHeavyModel = 16L * 1024 * 1024 * 1024;

    /// <summary>مدة صلاحية التقييم — تمنع تكرار استكشاف Ollama عند كل تحديث للواجهة.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan InternetCacheTtl = TimeSpan.FromSeconds(60);

    private readonly ILlmProvider? _localLlmProvider;
    private readonly object _gate = new();
    private DeviceCapabilityAssessment? _cached;
    private DateTimeOffset _cachedAt;
    private bool? _internetCached;
    private DateTimeOffset _internetCachedAt;

    public DeviceCapabilityEvaluator(ILlmProvider? localLlmProvider = null)
    {
        _localLlmProvider = localLlmProvider;
    }

    public async Task<DeviceCapabilityAssessment> AssessAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheTtl)
                return _cached;
        }

        var assessment = await EvaluateAsync(ct);

        lock (_gate)
        {
            _cached = assessment;
            _cachedAt = DateTimeOffset.UtcNow;
        }

        return assessment;
    }

    /// <summary>إلغاء الذاكرة المؤقتة — يُستدعى بعد تغيير إعدادات المساعد المحلي.</summary>
    public void InvalidateCache()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }

    private async Task<DeviceCapabilityAssessment> EvaluateAsync(CancellationToken ct)
    {
        long totalRam = GetTotalRamBytes();
        bool ollamaAvailable = false;
        bool internetAvailable = await CheckInternetAvailabilityAsync(ct);

        if (_localLlmProvider is not null)
        {
            try
            {
                ollamaAvailable = await _localLlmProvider.IsAvailableAsync(ct);
            }
            catch
            {
                ollamaAvailable = false;
            }
        }

        var tier = DetermineTier(totalRam, ollamaAvailable);
        var reason = BuildReason(totalRam, ollamaAvailable, internetAvailable, tier);

        return new DeviceCapabilityAssessment(
            Tier: tier,
            TotalRamBytes: totalRam,
            OllamaAvailable: ollamaAvailable,
            InternetAvailable: internetAvailable,
            Reason: reason);
    }

    /// <summary>
    /// الذاكرة الفعلية المتاحة للنظام (أو حد الحاوية). نستخدم GC.GetGCMemoryInfo
    /// لأنه متاح عبر الأنظمة دون APIs خاصة، ولا يعتمد على حجم العملية الحالية.
    /// </summary>
    private static long GetTotalRamBytes()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return info.TotalAvailableMemoryBytes;
        }
        catch
        {
            // تجاهل — سنستخدم تقديراً محافظاً أدناه
        }

        return 0;
    }

    private async Task<bool> CheckInternetAvailabilityAsync(CancellationToken ct)
    {
        if (_internetCached.HasValue && DateTimeOffset.UtcNow - _internetCachedAt < InternetCacheTtl)
            return _internetCached.Value;

        bool result;
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(
                "https://openrouter.ai",
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                ct);

            // أي استجابة HTTP تعني وجود اتصال بالشبكة، حتى لو كانت 4xx/5xx
            result = true;
        }
        catch
        {
            result = false;
        }

        _internetCached = result;
        _internetCachedAt = DateTimeOffset.UtcNow;
        return result;
    }

    private static DeviceCapabilityTier DetermineTier(long ramBytes, bool ollamaAvailable)
    {
        // الأساس هو توفر Ollama؛ الذاكرة تحدد حجم النموذج المناسب فقط.
        if (!ollamaAvailable) return DeviceCapabilityTier.None;
        if (ramBytes <= 0) return DeviceCapabilityTier.Light;

        if (ramBytes >= MinRamForHeavyModel) return DeviceCapabilityTier.Heavy;
        if (ramBytes >= MinRamForMediumModel) return DeviceCapabilityTier.Medium;
        if (ramBytes >= MinRamForLocalModel) return DeviceCapabilityTier.Light;

        // ذاكرة منخفضة لكن Ollama يعمل: نسمح بأصغر نموذج بدل حجب المسار المحلي كلياً.
        return DeviceCapabilityTier.Light;
    }

    private static string BuildReason(long ramBytes, bool ollamaAvailable, bool internetAvailable, DeviceCapabilityTier tier)
    {
        var ramGb = ramBytes / (1024.0 * 1024 * 1024);

        return tier switch
        {
            DeviceCapabilityTier.Heavy => $"الجهاز قادر ({ramGb:F1} غيغابايت RAM، Ollama متاح) — يدعم نموذجاً محلياً متوسطاً أو ثقيلاً.",
            DeviceCapabilityTier.Medium => $"الجهاز قادر ({ramGb:F1} غيغابايت RAM، Ollama متاح) — يدعم نموذجاً محلياً متوسطاً (حتى 7B).",
            DeviceCapabilityTier.Light => $"الجهاز قادر ({ramGb:F1} غيغابايت RAM، Ollama متاح) — يدعم نموذجاً محلياً خفيفاً للسرعة.",
            DeviceCapabilityTier.None when !ollamaAvailable => "Ollama غير متاح — شغّل تطبيق Ollama أو تحقق من أنه يعمل على المنفذ 11434.",
            _ => "تقييم غير محدد."
        };
    }
}

/// <summary>
/// مُتبّع الحصة المجانية للاستخدام السحابي.
/// يضمن عدم تجاوز الحصة المحددة، مع أولوية للمجانية التامة.
/// </summary>
public sealed class QuotaTracker : IQuotaTracker
{
    private readonly int? _monthlyLimit;
    private int _usedThisMonth;

    public QuotaTracker(int? monthlyFreeTokenLimit = null)
    {
        _monthlyLimit = monthlyFreeTokenLimit;
        _usedThisMonth = 0;
    }

    public int UsedTokensThisMonth => _usedThisMonth;

    public int RemainingTokens => _monthlyLimit.HasValue
        ? Math.Max(0, _monthlyLimit.Value - _usedThisMonth)
        : int.MaxValue;

    public bool IsQuotaExceeded => _monthlyLimit.HasValue && _usedThisMonth >= _monthlyLimit.Value;

    public bool RecordUsage(int tokens)
    {
        if (_monthlyLimit.HasValue && _usedThisMonth + tokens > _monthlyLimit.Value)
            return false;

        _usedThisMonth += tokens;
        return true;
    }

    public bool CanAfford(int tokens)
    {
        if (_monthlyLimit.HasValue && _usedThisMonth + tokens > _monthlyLimit.Value)
            return false;
        return true;
    }

    public void Reset()
    {
        _usedThisMonth = 0;
    }
}

/// <summary>
/// سياسة الإرجاع الآمن عند تعطل المسار.
/// عدد محاولات محدود مع مهلة واضحة، لا حلقات تبديل لانهائية.
/// </summary>
public sealed class FallbackPolicy : IFallbackPolicy
{
    private readonly int _maxAttempts;

    public FallbackPolicy(int maxAttempts = 2)
    {
        _maxAttempts = maxAttempts;
    }

    public bool ShouldRetry(string? errorMessage, int attemptNumber, int maxAttempts)
    {
        if (attemptNumber >= maxAttempts) return false;
        if (string.IsNullOrWhiteSpace(errorMessage)) return false;

        var lower = errorMessage.ToLowerInvariant();
        // لا نعيد المحاولة على أخطاء المصادقة أو الحصة المنتهية
        if (lower.Contains("unauthorized") || lower.Contains("forbidden") || lower.Contains("quota") || lower.Contains("rate limit"))
            return false;

        return true;
    }

    public RouteType GetFallbackRoute(RouteType failedRoute)
    {
        return failedRoute switch
        {
            RouteType.Cloud => RouteType.Local,   // السحابي تعطل → جرب المحلي
            RouteType.Local => RouteType.BuiltIn, // المحلي تعطل → ارجع للقواعد المدمجة
            RouteType.BuiltIn => RouteType.None,  // المدمج تعطل → لا شيء
            _ => RouteType.None
        };
    }
}
