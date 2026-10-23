namespace Wizz.Application.Abstractions;

using Wizz.Application.DTOs;

/// <summary>
/// موجه النماذج المستقل — يختار المسار الأنسب لكل طلب ذكاء اصطناعي
/// (قواعد برمجية مدمجة / نموذج محلي / مزود سحابي / لا شيء)
/// دون ربط بقية التطبيق بتفاصيل التنفيذ.
/// </summary>
public interface IModelRouter
{
    /// <summary>توجيه طلب الذكاء الاصطناعي وإرجاع النتيجة مع بيانات المسار.</summary>
    Task<ModelRouteResult> RouteAsync(AiQueryInputDto input, CancellationToken ct = default);

    /// <summary>هل يوجد مسار مؤهل للطلب المحدد؟</summary>
    Task<bool> HasCapableRouteAsync(AiQueryInputDto input, CancellationToken ct = default);

    /// <summary>حالة جميع المسارات المتاحة للعرض في واجهة المستخدم.</summary>
    Task<RouterStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>اختبار الاتصال بالسحابة وإرجاع رسالة تشخيصية (مهلة 15 ثانية).</summary>
    Task<(bool Success, string Message)> TestCloudConnectionAsync(CancellationToken ct = default);

    /// <summary>النماذج المحلية المثبتة (للعرض في قائمة الاختيار).</summary>
    Task<IReadOnlyList<string>> GetAvailableLocalModelsAsync(CancellationToken ct = default);

    /// <summary>تعيين النموذج المحلي النشط (يُستخدم عند اختياره من الواجهة).</summary>
    Task SetLocalModelAsync(string modelName, CancellationToken ct = default);
}

/// <summary>نتيجة توجيه طلب الذكاء الاصطناعي عبر المسار المختار.</summary>
public sealed record ModelRouteResult(
    string Answer,
    string ProviderName,
    string ModelName,
    RouteType RouteType,
    bool UsedStoreData,
    IReadOnlyList<string> ReferencedData,
    TimeSpan ResponseTime,
    bool IsFallback,
    string? ErrorMessage = null,
    RouteDecision? Decision = null);

/// <summary>مرشح مسار واحد: متاح أو لا، درجته، وسبب ظهوره/استبعاده.</summary>
/// <param name="Route">المسار.</param>
/// <param name="Eligible">هل يمكن اختياره لهذا الطلب؟</param>
/// <param name="Score">درجة الوزن (أولوية + جودة)؛ صفر لغير المتاح.</param>
/// <param name="Reason">سبب عربي واضح يظهر للمستخدم.</param>
/// <param name="Checked">هل فُحص المسار فعلاً؟ (false = استُبعد قبل الفحص لتوفير الوقت).</param>
public sealed record RouteCandidate(RouteType Route, bool Eligible, int Score, string Reason, bool Checked = true);

/// <summary>قرار التوجيه: المسار المختار وسبب اختياره مع كل المرشحين بترتيب الدرجات.</summary>
public sealed record RouteDecision(RouteType Chosen, string Reason, IReadOnlyList<RouteCandidate> Candidates)
{
    /// <summary>قرار اختيار يدوي من المستخدم (لا منافسة تلقائية).</summary>
    public static RouteDecision Manual(RouteType route)
        => new(route, "اختيار يدوي من المستخدم", Array.Empty<RouteCandidate>());
}

/// <summary>نوع المسار المستخدم في الاستجابة.</summary>
public enum RouteType
{
    /// <summary>قواعد برمجية مدمجة بدون نموذج (سريع، بدون إنترنت).</summary>
    BuiltIn = 0,

    /// <summary>نموذج محلي عبر Ollama.</summary>
    Local = 1,

    /// <summary>مزود سحابي مجاني.</summary>
    Cloud = 2,

    /// <summary>لا مسار مؤهل — إرشاد للمستخدم فقط.</summary>
    None = 3
}

/// <summary>حالة الموجه وعرض المسارات المتاحة.</summary>
public sealed record RouterStatusDto(
    bool BuiltInAvailable,
    bool LocalAvailable,
    string? LocalModel,
    bool CloudAvailable,
    string? CloudProvider,
    int RouteCount,
    string SummaryMessage);

/// <summary>مزود إعدادات الذكاء الاصطناعي — يقرأ الإعدادات من مصدر مركب (ملف إعدادات أو خدمة مركزية).</summary>
public interface IAiSettingsProvider
{
    /// <summary>هل الذكاء الاصطناعي السحابي مفعل من قبل المستخدم؟</summary>
    bool IsCloudEnabled { get; set; }

    /// <summary>النموذج المحلي المفضل (يمكن أن يكون فارغاً للاختيار التلقائي).</summary>
    string? PreferredLocalModel { get; }

    /// <summary>
    /// مستوى النموذج المحلي: "light" للأجهزة الضعيفة (أخف نموذج) أو "medium" (أكبر نموذج معقول).
    /// </summary>
    string LocalModelLevel { get; set; }

    /// <summary>مزوّد السحابة المفضل (يمكن أن يكون فارغاً للاختيار التلقائي).</summary>
    string? PreferredCloudProvider { get; }

    /// <summary>الحد الأقصى للرموز المجانية في الشهر (null = غير محدد / غير محدود).</summary>
    int? MonthlyFreeTokenLimit { get; }

    /// <summary>الرموز المستخدمة هذا الشهر.</summary>
    int UsedTokensThisMonth { get; }

    /// <summary>تسجيل استخدام الرموز.</summary>
    void RecordTokenUsage(int tokens);

    /// <summary>هل تجاوزت الحصة المجانية المحددة؟</summary>
    bool IsQuotaExceeded { get; }

    /// <summary>الحصول على هاش إعدادات السحابة للكشف عن التغييرات.</summary>
    string? GetCloudConfigHash();

    /// <summary>الحصول على إعدادات مزودي السحابة.</summary>
    IEnumerable<CloudProviderConfig>? GetCloudProvidersConfig();

    /// <summary>الحصول على مفتاح API السحابي الحالي (إذا كان مخزناً).</summary>
    string? GetCloudApiKey();

    /// <summary>تعيين مفتاح API السحابي.</summary>
    void SetCloudApiKey(string apiKey);
}

/// <summary>مستوى قدرة الجهاز على تشغيل نموذج محلي.</summary>
public enum DeviceCapabilityTier
{
    /// <summary>لا يمكن تشغيل نموذج محلي (أقل من 4 غيغابايت RAM أو Ollama غير متوفر).</summary>
    None = 0,

    /// <summary>يمكن تشغيل نموذج خفيف جداً (0.5B – 1.5B).</summary>
    Light = 1,

    /// <summary>يمكن تشغيل نموذج متوسط (2B – 4B).</summary>
    Medium = 2,

    /// <summary>يمكن تشغيل نموذج أكبر (7B 이상).</summary>
    Heavy = 3
}

/// <summary>نتيجة تقييم قدرة الجهاز.</summary>
public sealed record DeviceCapabilityAssessment(
    DeviceCapabilityTier Tier,
    long TotalRamBytes,
    bool OllamaAvailable,
    bool InternetAvailable,
    string Reason);

/// <summary>مقيّم قدرة الجهاز على تشغيل نماذج الذكاء الاصطناعي المحلية.</summary>
public interface IDeviceCapabilityEvaluator
{
    /// <summary>تقييم قدرة الجهاز الحالي.</summary>
    Task<DeviceCapabilityAssessment> AssessAsync(CancellationToken ct = default);

    /// <summary>إلغاء الذاكرة المؤقتة لتقييم قدرة الجهاز.</summary>
    void InvalidateCache();
}

/// <summary>مُتبّع الحصة المجانية للاستخدام السحابي.</summary>
public interface IQuotaTracker
{
    /// <summary>تسجيل استخدام الرموز والتحقق من عدم تجاوز الحصة.</summary>
    bool RecordUsage(int tokens);

    /// <summary>هل الحصة متاحة لطلب بحجم الرموز المحدد؟</summary>
    bool CanAfford(int tokens);

    /// <summary>الرموز المتبقية هذا الشهر.</summary>
    int RemainingTokens { get; }

    /// <summary>هل تجاوزت الحصة المجانية المحددة؟</summary>
    bool IsQuotaExceeded { get; }

    /// <summary>إعادة تعيين العداد (مثال: بداية شهر جديد).</summary>
    void Reset();
}

/// <summary>إعدادات مزود سحابي واحد.</summary>
public sealed record CloudProviderConfig(
    string Name,
    string DisplayName,
    string BaseUrl,
    string ApiKey,
    string? Model,
    bool Enabled = true);

/// <summary>
/// مصنع موفري النماذج — يسمح لطبقة Application بطلب موفر سحابي
/// دون معرفة تفاصيل التنفيذ (طبقة Infrastructure تسجّل التنفيذ).
/// </summary>
public interface ILlmProviderFactory
{
    /// <summary>إنشاء موفر سحابي من إعداده، أو null إذا كان الإعداد ناقصاً.</summary>
    ILlmProvider? CreateCloudProvider(CloudProviderConfig config);
}

/// <summary>سياسة الإرجاع الآمن (fallback) عند تعطل المسارات.</summary>
public interface IFallbackPolicy
{
    /// <summary>تحديد ما إذا كان ينبغي إعادة المحاولة مع مسار بديل.</summary>
    bool ShouldRetry(string? errorMessage, int attemptNumber, int maxAttempts);

    /// <summary>المسار البديل المقترح بعد الفشل.</summary>
    RouteType GetFallbackRoute(RouteType failedRoute);
}