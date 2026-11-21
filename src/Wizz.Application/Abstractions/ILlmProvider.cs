namespace Wizz.Application.Abstractions;

/// <summary>موفر نموذج لغوي — محلي (Ollama) أو سحابي، قابل للتبديل.</summary>
public interface ILlmProvider
{
    /// <summary>اسم الموفر (مثلاً "Ollama"، "OpenAI"، "Local").</summary>
    string ProviderName { get; }

    /// <summary>هل الموفر متاح وجاهز؟</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>إرسال استفسار والحصول على رد نصي.</summary>
    Task<string> CompleteAsync(string prompt, CancellationToken ct = default);

    /// <summary>إرسال استفسار مع سياق (بيانات المتجر) والحصول على رد.</summary>
    Task<string> CompleteWithContextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>الحصول على النماذج المتاحة.</summary>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default);

    /// <summary>تعيين النموذج المستخدم.</summary>
    Task SetModelAsync(string modelName, CancellationToken ct = default);

    /// <summary>
    /// اختبار الاتصال بالموفر وإرجاع رسالة تشخيصية واضحة للمستخدم.
    /// التنفيذ الافتراضي يستعمل <see cref="IsAvailableAsync"/>؛ الموفرون يعطون رسالة أدق.
    /// </summary>
    async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var available = await IsAvailableAsync(ct);
            return available
                ? (true, $"«{ProviderName}» متاح وجاهز.")
                : (false, $"«{ProviderName}» غير متاح حالياً.");
        }
        catch (Exception ex)
        {
            return (false, $"تعذّر الاتصال بـ«{ProviderName}»: {ex.Message}");
        }
    }

    /// <summary>
    /// اختبار تشخيصي يفرّق بين الوصول إلى كتالوج النماذج العامة وصلاحية مفتاح API لدى المزوّد.
    /// قيمة <c>KeyValid = null</c> تعني أن المزوّد لا يوفّر فحصًا منفصلًا للمفتاح، فتبقى الصلاحية غير محسومة.
    /// التنفيذ الافتراضي لا يفتح طلبًا إضافيًا، ويبقي نتيجة المفتاح غير محسومة.
    /// </summary>
    async Task<(bool CatalogReachable, bool? KeyValid, string Details)> TestConnectionDetailedAsync(CancellationToken ct = default)
    {
        var reachable = await IsAvailableAsync(ct);
        return (reachable, null, reachable
            ? "الوصول إلى كتالوج النماذج متاح، ولم يتحقق من صلاحية المفتاح."
            : "كتالوج النماذج غير متاح.");
    }
}

/// <summary>أداة AI محددة — تُستدعى من قبل النموذج عبر Function Calling.</summary>
public interface IAiTool
{
    string Name { get; }
    string Description { get; }
    string ParametersJsonSchema { get; }
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}

/// <summary>سجل أدوات AI — يدير الأدوات المتاحة للنموذج.</summary>
public interface IAiToolRegistry
{
    void Register(IAiTool tool);
    IAiTool? GetTool(string name);
    IReadOnlyList<IAiTool> GetAllTools();
}