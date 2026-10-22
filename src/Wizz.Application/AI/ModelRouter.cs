namespace Wizz.Application.AI;

using System.Diagnostics;
using Wizz.Application.Abstractions;
using Wizz.Application.DTOs;

public sealed class ModelRouter : IModelRouter
{
    private readonly BuiltInAssistant _builtIn;
    private readonly ILlmProvider? _localLlm;
    private readonly IAiSettingsProvider _settings;
    private readonly IDeviceCapabilityEvaluator _deviceEvaluator;
    private readonly IQuotaTracker _quotaTracker;
    private readonly IFallbackPolicy _fallbackPolicy;
    private readonly ILlmProviderFactory _providerFactory;

    private readonly object _cloudGate = new();
    private string _lastCloudConfigHash = "";
    private List<ILlmProvider> _cachedCloudProviders = new();

    private readonly object _statusGate = new();
    private DateTimeOffset _cloudCheckedAt = DateTimeOffset.MinValue;
    private bool _cloudReachable;
    private string _cloudCheckedHash = "";
    private string? _lastCloudError;

    public ModelRouter(
        BuiltInAssistant builtIn,
        ILlmProvider? localLlm,
        IAiSettingsProvider settings,
        IDeviceCapabilityEvaluator deviceEvaluator,
        IQuotaTracker quotaTracker,
        IFallbackPolicy fallbackPolicy,
        ILlmProviderFactory providerFactory)
    {
        _builtIn = builtIn;
        _localLlm = localLlm;
        _settings = settings;
        _deviceEvaluator = deviceEvaluator;
        _quotaTracker = quotaTracker;
        _fallbackPolicy = fallbackPolicy;
        _providerFactory = providerFactory;
    }

    public async Task<ModelRouteResult> RouteAsync(AiQueryInputDto input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Question))
            return Fail("اكتب سؤالاً أولاً.", RouteType.None, false);

        var sw = Stopwatch.StartNew();

        // توجيه مُجبر (اختيار يدوي من المستخدم)
        if (input.ForceRoute is { } forced && forced != RouteType.None)
        {
            if (forced == RouteType.BuiltIn)
            {
                var forcedBuiltIn = await TryBuiltInAsync(input, ct);
                sw.Stop();
                if (forcedBuiltIn is not null)
                    return new ModelRouteResult(forcedBuiltIn, "المساعد المدمج (بدون نموذج)", "built-in", RouteType.BuiltIn, true, new[] { "بيانات محلية" }, sw.Elapsed, IsFallback: false);
                return Fail("المساعد المدمج لم يستطع الإجابة عن هذا السؤال.", RouteType.None, true);
            }

            var forcedDevice = await _deviceEvaluator.AssessAsync(ct);

            if (forced == RouteType.Local)
            {
                if (!forcedDevice.OllamaAvailable || _localLlm is null)
                {
                    sw.Stop();
                    return Fail("النموذج المحلي (Ollama) غير متاح. شغّل Ollama وثبّت نموذجاً.", RouteType.None, true);
                }
                var localResult = await TryProviderAsync(_localLlm, input, RouteType.Local, ct);
                sw.Stop();
                if (localResult is not null)
                    return localResult with { ResponseTime = sw.Elapsed };
                var builtInFallback = await TryBuiltInAsync(input, ct);
                if (builtInFallback is not null)
                    return new ModelRouteResult(builtInFallback, "المساعد المدمج (بدون نموذج)", "built-in", RouteType.BuiltIn, true, new[] { "بيانات محلية" }, sw.Elapsed, IsFallback: true);
                return Fail("تعذّر الحصول على إجابة من النموذج المحلي.", RouteType.None, true);
            }

            if (forced == RouteType.Cloud)
            {
                if (!_settings.IsCloudEnabled)
                {
                    sw.Stop();
                    return Fail("السحابة غير مفعّلة. فعّلها من «الإعدادات».", RouteType.None, true);
                }
                var providers = RefreshCloudProvidersIfNeeded();
                if (!providers.Any())
                {
                    sw.Stop();
                    return Fail("لا يوجد مزود سحابي مُهيّأ. أدخل مفتاح API في «الإعدادات».", RouteType.None, true);
                }
                foreach (var provider in providers)
                {
                    var cloudResult = await TryProviderAsync(provider, input, RouteType.Cloud, ct);
                    if (cloudResult is not null)
                    {
                        var estimatedTokens = EstimateTokens(input.Question + cloudResult.Answer);
                        _quotaTracker.RecordUsage(estimatedTokens);
                        sw.Stop();
                        return cloudResult with { ResponseTime = sw.Elapsed };
                    }
                }
                sw.Stop();
                var builtInFallback2 = await TryBuiltInAsync(input, ct);
                if (builtInFallback2 is not null)
                    return new ModelRouteResult(builtInFallback2, "المساعد المدمج (بدون نموذج)", "built-in", RouteType.BuiltIn, true, new[] { "بيانات محلية" }, sw.Elapsed, IsFallback: true);
                return Fail("تعذّر الحصول على إجابة من السحابة.", RouteType.None, true);
            }
        }

        // ===== التوجيه التلقائي (auto) =====
        var builtInAnswer = await TryBuiltInAsync(input, ct);
        if (builtInAnswer is not null)
        {
            sw.Stop();
            return new ModelRouteResult(
                builtInAnswer, "المساعد المدمج (بدون نموذج)", "built-in",
                RouteType.BuiltIn, true, new[] { "بيانات محلية" }, sw.Elapsed, IsFallback: false);
        }

        var device = await _deviceEvaluator.AssessAsync(ct);

        // 1) النموذج المحلي (الأسرع والأخاص) — أولوية local-first
        if (device.OllamaAvailable && _localLlm is not null)
        {
            var localResult = await TryProviderAsync(_localLlm, input, RouteType.Local, ct);
            if (localResult is not null)
            {
                sw.Stop();
                return new ModelRouteResult(
                    localResult.Answer, localResult.ProviderName, localResult.ModelName,
                    localResult.RouteType, localResult.UsedStoreData, localResult.ReferencedData,
                    sw.Elapsed, localResult.IsFallback, localResult.ErrorMessage);
            }
        }

        // 2) السحابة: فقط إذا فعّلها المستخدم وأُعدّ مفتاح صالح (وتعذّر المحلي أو غير متاح)
        if (_settings.IsCloudEnabled && device.InternetAvailable && !_quotaTracker.IsQuotaExceeded)
        {
            var cloudProviders = RefreshCloudProvidersIfNeeded();
            if (!_quotaTracker.IsQuotaExceeded)
            {
                foreach (var provider in cloudProviders)
                {
                    var cloudResult = await TryProviderAsync(provider, input, RouteType.Cloud, ct);
                    if (cloudResult is not null)
                    {
                        var estimatedTokens = EstimateTokens(input.Question + cloudResult.Answer);
                        if (_quotaTracker.RecordUsage(estimatedTokens))
                        {
                            sw.Stop();
                            return cloudResult with { ResponseTime = sw.Elapsed };
                        }
                        break;
                    }
                }
            }
        }

        sw.Stop();
        var hint = BuildHint(device, _settings.IsCloudEnabled, _lastCloudError);
        return new ModelRouteResult(hint, "المساعد المدمج (بدون نموذج)", "built-in", RouteType.None, false, Array.Empty<string>(), sw.Elapsed, IsFallback: true, "لا يوجد نموذج متاح للإجابة على هذا السؤال.");
    }

    public async Task<bool> HasCapableRouteAsync(AiQueryInputDto input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Question)) return false;
        if (await _builtIn.TryAnswerAsync(input.Question, ct) is not null) return true;

        var device = await _deviceEvaluator.AssessAsync(ct);
        if (device.OllamaAvailable && _localLlm is not null) return true;

        if (_settings.IsCloudEnabled && device.InternetAvailable)
        {
            var providers = RefreshCloudProvidersIfNeeded();
            if (providers.Any()) return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<string>> GetAvailableLocalModelsAsync(CancellationToken ct = default)
    {
        if (_localLlm is null) return Array.Empty<string>();
        try
        {
            var models = await _localLlm.GetAvailableModelsAsync(ct);
            return models;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task SetLocalModelAsync(string modelName, CancellationToken ct = default)
    {
        if (_localLlm is null || string.IsNullOrWhiteSpace(modelName)) return;
        try
        {
            await _localLlm.SetModelAsync(modelName, ct);
        }
        catch
        {
            // تجاهل — الواجهة تُظهر الخطأ عبر RefreshStatus
        }
    }

    public async Task<(bool Success, string Message)> TestCloudConnectionAsync(CancellationToken ct = default)
    {
        if (!_settings.IsCloudEnabled)
            return (false, "السحابة غير مفعّلة. فعّلها من «الإعدادات».");

        var providers = RefreshCloudProvidersIfNeeded();
        if (!providers.Any())
            return (false, "لا يوجد مزود سحابي مُهيّأ. أدخل مفتاح API في «الإعدادات».");

        var first = providers.First();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var ok = await first.IsAvailableAsync(cts.Token);
            if (ok)
                return (true, $"✅ الاتصال بالسحابة ({first.ProviderName}) ناجح.");
            return (false, $"❌ السحابة ({first.ProviderName}) لم تستجب. تحقق من المفتاح والاتصال.");
        }
        catch (Exception ex)
        {
            return (false, $"❌ خطأ في الاتصال: {ex.Message}");
        }
    }

    public async Task<RouterStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var device = await _deviceEvaluator.AssessAsync(ct);

        bool localAvailable = false;
        string? localModel = null;
        if (_localLlm is not null)
        {
            try
            {
                localAvailable = await _localLlm.IsAvailableAsync(ct);
                localModel = _localLlm.ProviderName;
            }
            catch { localAvailable = false; }
        }

        bool cloudAvailable = false;
        string? cloudProvider = null;
        bool cloudConfiguredButNoKey = false;
        if (_settings.IsCloudEnabled)
        {
            var providers = RefreshCloudProvidersIfNeeded();
            var configured = _settings.GetCloudProvidersConfig()?.Any() == true;
            if (!providers.Any() && configured) cloudConfiguredButNoKey = true;
            if (providers.Any())
            {
                var first = providers.First();
                cloudProvider = first.ProviderName;
                // تحديد متاح (مُهيأ) — لا نستكشف الشبكة هنا لتجنب التأخير
                cloudAvailable = true;
                // اختبار سرعة (اختياري): استكشاف قصير
                // لا استكشاف شبكة هنا - التحقق الفعلي عبر TestCloudConnectionAsync
            }
        }

        int routeCount = 1; // المدمج دائماً متاح
        if (localAvailable) routeCount++;
        if (cloudAvailable) routeCount++;

        var summary = routeCount switch
        {
            1 when cloudConfiguredButNoKey => "المساعد المدمج فقط — فعّلت السحابة لكن لم تُدخل مفتاح API. الصقه في «الإعدادات» ثم احفظ.",
            1 when !device.OllamaAvailable => "المساعد المدمج فقط — Ollama غير متاح. ثبت Ollama (ollama.com) ثم نموذجاً (ollama pull qwen2.5:0.5b-instruct).",
            1 => "المساعد المدمج فقط متاح.",
            2 when localAvailable => "المدمج والنموذج المحلي متاحان.",
            _ => "جميع المسارات متاحة: المدمج، المحلي، والسحابي."
        };

        return new RouterStatusDto(true, localAvailable, localModel, cloudAvailable, cloudProvider, routeCount, summary);
    }

    private async Task<string?> TryBuiltInAsync(AiQueryInputDto input, CancellationToken ct)
    {
        try { return await _builtIn.TryAnswerAsync(input.Question, ct); } catch { return null; }
    }

    private async Task<ModelRouteResult?> TryProviderAsync(ILlmProvider provider, AiQueryInputDto input, RouteType routeType, CancellationToken ct)
    {
        try
        {
            var snapshot = await _builtIn.GetSnapshotAsync(ct);
            var systemPrompt = BuildSystemPrompt(snapshot, input.Question);
            var answer = await provider.CompleteWithContextAsync(systemPrompt, input.Question, ct);
            if (string.IsNullOrWhiteSpace(answer)) return null;
            return new ModelRouteResult(answer.Trim(), provider.ProviderName, GetActiveModelName(provider), routeType, true, new[] { "مبيعات", "ديون", "منتجات" }, TimeSpan.Zero, IsFallback: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception ex) { _lastCloudError = ex.Message; return null; }
    }

    private static string BuildSystemPrompt(StoreSnapshot snapshot, string userQuestion)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("أنت «مساعد Wizz»، مساعد نقاط بيع جزائري يعمل محلياً.");
        sb.AppendLine("قواعد:");
        sb.AppendLine("1) أجب عن السؤال المحدد فقط، ولا تنسخ إجابة عامة ولا تكرر نفس المقدمة في كل مرة.");
        sb.AppendLine("2) استخدم الأرقام الواردة في «بيانات المتجر» حصراً؛ لا تخترع أي رقم أو اسم.");
        sb.AppendLine("3) إذا طُلب سعر منتج أو اسمه، ابحث عنه في قائمة المنتجات أدناه.");
        sb.AppendLine("4) إن لم تتوفر المعلومة، قل بصراحة إنها غير متوفرة وحدد الصفحة التي تُظهرها.");
        sb.AppendLine("5) لا تقترح حذفاً أو إلغاء بيع أو تغيير أسعار دون تنبيه المستخدم.");
        sb.AppendLine("6) أجب بالعربية، بإيجاز، وبقائمة مرقّمة عند طلب قائمة.");
        sb.AppendLine();
        sb.AppendLine(BuiltInAssistant.BuildContextText(snapshot));
        return sb.ToString();
    }

    private static string GetActiveModelName(ILlmProvider provider)
    {
        var name = provider.ProviderName;
        if (name.Contains("—", StringComparison.Ordinal) && name.Split('—').Length > 1)
            return name.Split('—')[1].Trim();
        return name;
    }

    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 3);

    private static string BuildHint(DeviceCapabilityAssessment device, bool cloudEnabled, string? lastCloudError)
    {
        var parts = new List<string> { "لم أفهم السؤال بما يكفي." };
        if (!device.OllamaAvailable) parts.Add("لتشغيل الأسئلة الحرة محليًا: شغّل Ollama وثبّت نموذجاً خفيفاً يدعم العربية (ollama pull qwen2.5:0.5b-instruct).");
        if (!cloudEnabled) parts.Add("لإضافة الأسئلة الحرة عبر السحابة: فعّل الذكاء الاصطناعي السحابي من الإعدادات ولصق مفتاح OpenRouter.");
        if (!string.IsNullOrWhiteSpace(lastCloudError)) parts.Add($"(تعذّر استخدام السحابة: {lastCloudError})");
        parts.Add("جرّب صيغة أوضح مثل: «قائمة الديون», «مجموع الديون», «أكبر دين», «مبيعات أمس», «أفضل 3 منتجات», «سعر تفاح».");
        return string.Join("\n\n", parts);
    }

    private List<ILlmProvider> RefreshCloudProvidersIfNeeded()
    {
        var currentHash = _settings.GetCloudConfigHash() ?? "";
        lock (_cloudGate)
        {
            if (currentHash == _lastCloudConfigHash && _cachedCloudProviders is not null)
                return _cachedCloudProviders;

            _lastCloudConfigHash = currentHash;
            _cachedCloudProviders = _settings.GetCloudProvidersConfig()
                ?.Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.ApiKey) && !string.IsNullOrWhiteSpace(c.BaseUrl))
                .Select(c => _providerFactory.CreateCloudProvider(c))
                .Where(p => p is not null)
                .Cast<ILlmProvider>()
                .ToList() ?? new List<ILlmProvider>();

            return _cachedCloudProviders;
        }
    }

    private ModelRouteResult Fail(string message, RouteType routeType, bool isFallback)
    {
        return new ModelRouteResult(message, "النظام", "none", routeType, false, Array.Empty<string>(), TimeSpan.Zero, isFallback, message);
    }
}