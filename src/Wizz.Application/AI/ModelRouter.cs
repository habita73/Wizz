namespace Wizz.Application.AI;

using System.Diagnostics;
using Wizz.Application.Abstractions;
using Wizz.Application.AI.Memory;
using Wizz.Application.AI.Orchestration;
using Wizz.Application.AI.Privacy;
using Wizz.Application.AI.Routing;
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
    private readonly IConversationMemory? _memory;
    private readonly UserPreferenceStore? _preferences;
    private readonly AutoRoutePriority? _priority;
    private readonly RouteDecisionLog? _decisionLog;
    private readonly CloudShareGuard _shareGuard;

    private string? _turnSubject;
    private BuiltInAnswer? _lastBuiltInAnswer;

    private readonly object _cloudGate = new();
    private string _lastCloudConfigHash = "";
    private List<ILlmProvider> _cachedCloudProviders = new();

    private readonly object _statusGate = new();
    private DateTimeOffset _cloudCheckedAt = DateTimeOffset.MinValue;
    private string? _lastCloudError;

    public ModelRouter(
        BuiltInAssistant builtIn,
        ILlmProvider? localLlm,
        IAiSettingsProvider settings,
        IDeviceCapabilityEvaluator deviceEvaluator,
        IQuotaTracker quotaTracker,
        IFallbackPolicy fallbackPolicy,
        ILlmProviderFactory providerFactory,
        IConversationMemory? memory = null,
        UserPreferenceStore? preferences = null,
        AutoRoutePriority? priority = null,
        RouteDecisionLog? decisionLog = null)
    {
        _builtIn = builtIn;
        _localLlm = localLlm;
        _settings = settings;
        _deviceEvaluator = deviceEvaluator;
        _quotaTracker = quotaTracker;
        _fallbackPolicy = fallbackPolicy;
        _providerFactory = providerFactory;
        _memory = memory;
        _preferences = preferences;
        _priority = priority;
        _decisionLog = decisionLog;
        _shareGuard = new CloudShareGuard(settings);
    }

    public async Task<ModelRouteResult> RouteAsync(AiQueryInputDto input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Question))
            return Fail("اكتب سؤالاً أولاً.", RouteType.None, false);

        _turnSubject = null;
        _lastBuiltInAnswer = null;
        var result = await RouteCoreAsync(input, ct);
        result = Decorate(input, result);
        RecordTurn(input.Question, result);
        return result;
    }

    /// <summary>
    /// يُرفق نتيجة التوجيه بشفافية أنماط العمل وتحكيم المدمج للأرقام. لا يغيّر قرار التوجيه أو النص،
    /// بل يضيف بيانات توضيحية فقط، مع بقاء حكم الأرقام كما هو في <see cref="RouteCoreAsync"/>.
    /// </summary>
    private ModelRouteResult Decorate(AiQueryInputDto input, ModelRouteResult result)
    {
        try
        {
            var isManual = input.ForceRoute is { } forced && forced != RouteType.None;
            var workMode = WorkModeResolver.Resolve(
                input.Question,
                _lastBuiltInAnswer,
                _localLlm is not null,
                _settings.IsCloudEnabled && _shareGuard.SharingEnabled,
                isManual,
                input.ForceRoute ?? RouteType.None);

            var transparency = WorkModeText.Transparency(workMode);
            var adjudicated = result.RouteType == RouteType.BuiltIn || _lastBuiltInAnswer is not null;
            transparency += adjudicated
                ? " — تحكيم الأرقام: تم"
                : " — تحكيم الأرقام: لا توجد أرقام متجر في هذا الرد";

            return result with
            {
                WorkMode = WorkModeText.Label(workMode.Mode),
                Transparency = transparency,
                Adjudicated = adjudicated
            };
        }
        catch
        {
            // الشفافية تحسين عرضي — لا تُفشل الإجابة أبداً.
            return result;
        }
    }

    private async Task<ModelRouteResult> RouteCoreAsync(AiQueryInputDto input, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // حزمة الحقائق تُبنى دائماً من إجابة المدمج قبل أي تفويض أو صياغة (الثابت 2 و3).
        var builtInAnswer = await TryBuiltInAsync(input, ct);
        var facts = FactPackage.FromBuiltIn(builtInAnswer);

        // توجيه مُجبر (اختيار يدوي من المستخدم)
        if (input.ForceRoute is { } forced && forced != RouteType.None)
        {
            var manual = RouteDecision.Manual(forced);
            LogDecision(input.Question, manual);

            if (forced == RouteType.BuiltIn)
            {
                var forcedBuiltIn = await TryBuiltInAsync(input, ct);
                sw.Stop();
                if (forcedBuiltIn is not null)
                    return new ModelRouteResult(forcedBuiltIn.Text, "المساعد المدمج (بدون نموذج)", "built-in", RouteType.BuiltIn, true, ReferencedData(forcedBuiltIn), sw.Elapsed, IsFallback: false, Decision: manual);
                return Fail("المساعد المدمج لم يستطع الإجابة عن هذا السؤال.", RouteType.None, true) with { Decision = manual };
            }

            var forcedDevice = await _deviceEvaluator.AssessAsync(ct);

            if (forced == RouteType.Local)
            {
                if (!forcedDevice.OllamaAvailable || _localLlm is null)
                {
                    sw.Stop();
                    return Fail("النموذج المحلي (Ollama) غير متاح. شغّل Ollama وثبّت نموذجاً.", RouteType.None, true) with { Decision = manual };
                }
                var localResult = await TryProviderAsync(_localLlm, input, RouteType.Local, facts, ct);
                sw.Stop();
                if (localResult is not null)
                    return localResult with { ResponseTime = sw.Elapsed, Decision = manual };
                var builtInFallback = await TryBuiltInAsync(input, ct);
                if (builtInFallback is not null)
                    return new ModelRouteResult(builtInFallback.Text, "المساعد المدمج (بدون نموذج)", "built-in", RouteType.BuiltIn, true, ReferencedData(builtInFallback), sw.Elapsed, IsFallback: true, Decision: manual);
                return Fail("تعذّر الحصول على إجابة من النموذج المحلي.", RouteType.None, true) with { Decision = manual };
            }

            if (forced == RouteType.Cloud)
            {
                if (!_settings.IsCloudEnabled || !_shareGuard.SharingEnabled)
                {
                    sw.Stop();
                    return Fail("السحابة غير مفعّلة. فعّل النموذج السحابي والتشارك السحابي من «الإعدادات».", RouteType.None, true) with { Decision = manual };
                }
                var providers = RefreshCloudProvidersIfNeeded();
                if (!providers.Any())
                {
                    sw.Stop();
                    return Fail("لا يوجد مزود سحابي مُهيّأ. أدخل مفتاح API في «الإعدادات».", RouteType.None, true) with { Decision = manual };
                }
                foreach (var provider in providers)
                {
                    var cloudResult = await TryProviderAsync(provider, input, RouteType.Cloud, facts, ct);
                    if (cloudResult is not null)
                    {
                        var estimatedTokens = EstimateTokens(input.Question + cloudResult.Answer);
                        _quotaTracker.RecordUsage(estimatedTokens);
                        sw.Stop();
                        return cloudResult with { ResponseTime = sw.Elapsed, Decision = manual };
                    }
                }
                sw.Stop();
                var builtInFallback2 = await TryBuiltInAsync(input, ct);
                if (builtInFallback2 is not null)
                    return new ModelRouteResult(builtInFallback2.Text, "المساعد المدمج (بدون نموذج)", "built-in", RouteType.BuiltIn, true, ReferencedData(builtInFallback2), sw.Elapsed, IsFallback: true, Decision: manual);
                return Fail("تعذّر الحصول على إجابة من السحابة.", RouteType.None, true) with { Decision = manual };
            }
        }

        // ===== التوجيه التلقائي: نقاط الأولوية (اختيار المستخدم) + درجة الجودة =====
        // حماية أسئلة الأرقام: إجابة مدمجة دقيقة (مبيعات/ديون/أسعار/أعداد/أدوات) لا يستبدلها نموذج لغوي،
        // حتى لو رفع المستخدم النموذج في الترتيب — لأن الأرقام أهم من صياغة النموذج.
        var protectBuiltIn = builtInAnswer is not null
            && BuiltInAssistant.IsPrecisionAnswer(builtInAnswer)
            && !BuiltInAssistant.IsAnalytical(input.Question);

        var priorityOrder = _priority ?? AutoRoutePriority.Default;

        // المسار الشائع (المدمج أولاً أو حماية الأرقام): لا حاجة لفحص Ollama ولا الشبكة — الإجابة تبقى فورية.
        if (builtInAnswer is not null && (protectBuiltIn || priorityOrder.IndexOf(RouteType.BuiltIn) == 0))
        {
            var quickDecision = protectBuiltIn
                ? RouteScorer.BuiltInProtected()
                : RouteScorer.BuiltInFirst("بيانات متجرك جاهزة محلياً دون إنترنت");
            LogDecision(input.Question, quickDecision);
            sw.Stop();
            return new ModelRouteResult(
                builtInAnswer.Text, "المساعد المدمج (بدون نموذج)", "built-in",
                RouteType.BuiltIn, true, ReferencedData(builtInAnswer), sw.Elapsed, IsFallback: false,
                Decision: quickDecision);
        }

        var device = await _deviceEvaluator.AssessAsync(ct);

        // أي إرسال سحابي يشترط تفعيل مفتاحَي النموذج والتشارك معاً؛ بدون التشارك لا يُبنى طلب.
        var cloudCanSend = _settings.IsCloudEnabled && _shareGuard.SharingEnabled;
        var cloudProviders = cloudCanSend
            ? RefreshCloudProvidersIfNeeded()
            : new List<ILlmProvider>();
        var quotaExceeded = _quotaTracker.IsQuotaExceeded;

        var availability = new RouteAvailability(
            BuiltInCanAnswer: builtInAnswer is not null,
            LocalAvailable: device.OllamaAvailable && _localLlm is not null,
            DeviceTier: device.Tier,
            InternetAvailable: device.InternetAvailable,
            CloudEnabled: _settings.IsCloudEnabled,
            CloudAvailable: cloudCanSend && device.InternetAvailable && !quotaExceeded && cloudProviders.Count > 0,
            QuotaExceeded: quotaExceeded,
            IsAnalytical: BuiltInAssistant.IsAnalytical(input.Question),
            ProtectBuiltIn: protectBuiltIn);

        var decision = RouteScorer.Choose(availability, _priority);
        LogDecision(input.Question, decision);

        // نجرّب المرشحين بترتيب الدرجة: إن فشل الأعلى ننتقل إلى الذي يليه.
        foreach (var candidate in decision.Candidates.Where(c => c.Eligible))
        {
            if (candidate.Route == RouteType.BuiltIn && builtInAnswer is not null)
            {
                sw.Stop();
                return new ModelRouteResult(
                    builtInAnswer.Text, "المساعد المدمج (بدون نموذج)", "built-in",
                    RouteType.BuiltIn, true, ReferencedData(builtInAnswer), sw.Elapsed, IsFallback: false,
                    Decision: decision);
            }

            if (candidate.Route == RouteType.Local && device.OllamaAvailable && _localLlm is not null)
            {
                var localResult = await TryProviderAsync(_localLlm, input, RouteType.Local, facts, ct);
                if (localResult is not null)
                {
                    sw.Stop();
                    return localResult with { ResponseTime = sw.Elapsed, Decision = decision };
                }

                continue;
            }

            if (candidate.Route == RouteType.Cloud && availability.CloudAvailable)
            {
                foreach (var provider in cloudProviders)
                {
                    if (_quotaTracker.IsQuotaExceeded)
                    {
                        break;
                    }

                    var cloudResult = await TryProviderAsync(provider, input, RouteType.Cloud, facts, ct);
                    if (cloudResult is null)
                    {
                        continue;
                    }

                    var estimatedTokens = EstimateTokens(input.Question + cloudResult.Answer);
                    if (_quotaTracker.RecordUsage(estimatedTokens))
                    {
                        sw.Stop();
                        return cloudResult with { ResponseTime = sw.Elapsed, Decision = decision };
                    }

                    break;
                }
            }
        }

        sw.Stop();
        var hint = BuildHint(device, _settings.IsCloudEnabled, _lastCloudError);
        return new ModelRouteResult(
            hint, "المساعد المدمج (بدون نموذج)", "built-in", RouteType.None, false,
            Array.Empty<string>(), sw.Elapsed, IsFallback: true,
            ErrorMessage: "لا يوجد مسار متاح للإجابة على هذا السؤال.",
            Decision: decision);
    }

    /// <summary>يسجّل قرار التوجيه في السجل التشخيصي (لا يُفشل الطلب أبداً).</summary>
    private void LogDecision(string question, RouteDecision decision)
    {
        try
        {
            _decisionLog?.Add(new RouteDecisionEntry(
                DateTime.UtcNow,
                question,
                decision.Chosen,
                decision.Reason,
                decision.Candidates));
        }
        catch
        {
            // السجل تشخيصي فقط — لا يُعطّل الإجابة.
        }
    }

    public async Task<bool> HasCapableRouteAsync(AiQueryInputDto input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Question)) return false;
        if (await _builtIn.TryAnswerAsync(input.Question, ct) is not null) return true;

        var device = await _deviceEvaluator.AssessAsync(ct);
        if (device.OllamaAvailable && _localLlm is not null) return true;

        if (_settings.IsCloudEnabled && _shareGuard.SharingEnabled && device.InternetAvailable)
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
        if (_settings.IsCloudEnabled && _shareGuard.SharingEnabled)
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

    /// <summary>بيانات الإجابة المرجعية (تُعرض في الواجهة): بيانات المتجر، أو بحث الإنترنت.</summary>
    private static IReadOnlyList<string> ReferencedData(BuiltInAnswer answer)
        => answer.Intent == Tools.SearchWebTool.Intent
            ? new[] { "بحث إنترنت" }
            : new[] { "بيانات محلية" };

    private async Task<BuiltInAnswer?> TryBuiltInAsync(AiQueryInputDto input, CancellationToken ct)
    {
        try
        {
            var answer = await _builtIn.TryAnswerWithSubjectAsync(input.Question, ct);
            if (answer is not null)
            {
                _lastBuiltInAnswer = answer;
            }

            if (answer?.Subject is { Length: > 0 } subject)
            {
                _turnSubject = subject;
                if (RememberEnabled())
                {
                    _memory?.SetLastSubject(subject);
                }
            }

            return answer;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>هل تُحفظ ذاكرة المحادثة؟ (تحترم مفتاح الخصوصية «حفظ السياق»).</summary>
    private bool RememberEnabled() => _memory is not null && (_preferences is null || _preferences.RememberContext);

    /// <summary>تسجيل دورة المحادثة في الذاكرة (لا تُفشل الإجابة أبداً).</summary>
    private void RecordTurn(string question, ModelRouteResult result)
    {
        try
        {
            if (!RememberEnabled()) return;
            if (string.IsNullOrWhiteSpace(result.Answer)) return;
            if (result.RouteType == RouteType.None) return;

            _memory!.Add(new ConversationTurn(
                question,
                result.Answer,
                DateTime.UtcNow,
                _turnSubject,
                result.RouteType.ToString()));
        }
        catch
        {
            // تعذّر حفظ الذاكرة لا يجب أن يُفشل الإجابة.
        }
    }

    private async Task<ModelRouteResult?> TryProviderAsync(
        ILlmProvider provider,
        AiQueryInputDto input,
        RouteType routeType,
        FactPackage facts,
        CancellationToken ct)
    {
        try
        {
            string systemPrompt;
            string userPrompt;
            bool usedStoreData;
            IReadOnlyList<string> references;

            if (routeType == RouteType.Cloud)
            {
                // قبل أي إرسال: تنقيح إلزامي للسؤال، ولا شيء يُرسل إطلاقاً بدون مفتاح التشارك.
                var payload = _shareGuard.TryPrepare(input.Question);
                if (payload is null) return null;
                systemPrompt = BuildSafeCloudPrompt(payload);
                userPrompt = payload.RedactedSummary;
                usedStoreData = false;
                references = new[] { "ملخص مجهول" };
            }
            else
            {
                // المحلي يعمل على الجهاز — لا إرسال للخارج، فيمكنه استخدام سياق المتجر.
                var snapshot = await _builtIn.GetSnapshotAsync(ct);
                systemPrompt = BuildSystemPrompt(snapshot, input.Question);
                userPrompt = input.Question;
                usedStoreData = true;
                references = new[] { "مبيعات", "ديون", "منتجات" };
            }

            var answer = await provider.CompleteWithContextAsync(systemPrompt, userPrompt, ct);
            if (string.IsNullOrWhiteSpace(answer)) return null;

            var draft = answer.Trim();
            if (!string.IsNullOrWhiteSpace(facts.AuthoritativeText))
            {
                // المدمج يُحكّم كل رقم نهائي؛ أي رقم غريب يُسقط الصياغة ويعيد نص المدمج.
                var adjudication = NumberAdjudicator.Adjudicate(draft, facts);
                if (!adjudication.Accepted) return null;
                draft = adjudication.FinalText;
            }

            return new ModelRouteResult(
                draft, provider.ProviderName, GetActiveModelName(provider), routeType,
                usedStoreData, references, TimeSpan.Zero, IsFallback: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception ex) { _lastCloudError = ex.Message; return null; }
    }

    /// <summary>
    /// تعليمات آمنة للسحابة: ملخص مجهول فقط، بلا سياق متجر ولا سؤال خام ولا أرقام مطلوبة.
    /// تُإرفق بفحص إضافي داخل مزوّد السحابة قبل أي طلب فعلي.
    /// </summary>
    private static string BuildSafeCloudPrompt(CloudSharePayload payload)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("أنت مساعد صياغة وتحليل يعمل بالملخص المجهول الآتي فقط.");
        sb.AppendLine("قواعد صارمة:");
        sb.AppendLine("1) لا تطلب بيانات إضافية، ولا تذكر أسماء أو معرّفات أو عناوين أو أرقاماً.");
        sb.AppendLine("2) لا تُنتج رقمًا نهائيًا؛ الأرقام تُحكَّم محليًا بواسطة المساعد المدمج.");
        sb.AppendLine("3) أجب بالعربية الفصحى، بإيجاز.");
        sb.AppendLine($"الغرض المسموح: {payload.Purpose}");
        sb.AppendLine($"الملخص المجهول: {payload.RedactedSummary}");
        return sb.ToString();
    }

    private string BuildSystemPrompt(StoreSnapshot snapshot, string userQuestion)
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

        var memoryContext = BuildMemoryContext();
        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            sb.AppendLine(memoryContext);
            sb.AppendLine();
        }

        sb.AppendLine(BuiltInAssistant.BuildContextText(snapshot));
        return sb.ToString();
    }

    /// <summary>سياق الذاكرة والتفضيلات الذي يُمرَّر للنموذج (يساعده على فهم أسئلة المتابعة).</summary>
    private string BuildMemoryContext()
    {
        var sb = new System.Text.StringBuilder();

        if (_preferences is not null)
        {
            var priceMode = _preferences.IsWholesalePreferred ? "الجملة" : "التجزئة";
            var unit = string.IsNullOrWhiteSpace(_preferences.PreferredUnit)
                ? string.Empty
                : $" وحدة القياس المفضّلة: {_preferences.PreferredUnit}.";
            sb.AppendLine($"تفضيلات المستخدم: اعرض سعر {priceMode} أولاً.{unit}");
        }

        if (RememberEnabled())
        {
            var turns = ConversationMemory.BuildContextText(_memory!.Recent(3));
            if (!string.IsNullOrWhiteSpace(turns))
            {
                sb.AppendLine(turns);
            }
        }

        return sb.ToString().TrimEnd();
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
        if (!cloudEnabled) parts.Add("لإضافة الأسئلة الحرة عبر السحابة: فعّل النموذج السحابي والتشارك السحابي في الإعدادات، ثم لصق مفتاح OpenRouter.");
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