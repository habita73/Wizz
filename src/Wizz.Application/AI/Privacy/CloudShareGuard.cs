namespace Wizz.Application.AI.Privacy;

using Wizz.Application.Abstractions;

/// <summary>ملخص مجهول جاهز للاستخدام في مهمة تحسين الصياغة والتحليل فقط.</summary>
/// <param name="RedactedSummary">الملخص بعد التنقيح.</param>
/// <param name="RemovedCategories">ما حُذف من السؤال.</param>
/// <param name="Purpose">الغرض المسموح به.</param>
public sealed record CloudSharePayload(
    string RedactedSummary,
    IReadOnlyList<string> RemovedCategories,
    string Purpose);

/// <summary>
/// بوابة التشارك السحابي: لا يُبنى أي طلب سحابي إلا بعد تنقيح إلزامي للسؤال،
/// ولا يُسمح بأي إرسال إلا إذا كان مفتاح «التشارك السحابي» مفعّلاً صراحةً.
/// المفتاحان مستقلان؛ هذا الصنف يفحص مفتاح التشارك فقط، ومفتاح النموذج يفحصه الموجّه.
/// </summary>
public sealed class CloudShareGuard
{
    /// <summary>الغرض الثابت المسموح به للتشارك.</summary>
    public const string AllowedPurpose = "تحسين الصياغة والتحليل";

    private readonly IAiSettingsProvider _settings;

    public CloudShareGuard(IAiSettingsProvider settings)
    {
        _settings = settings;
    }

    /// <summary>هل التشارك مفعّل صراحةً؟ (الافتراضي false).</summary>
    public bool SharingEnabled => _settings.CloudSharingEnabled;

    /// <summary>
    /// يحضّر حمولة آمنة، أو يعيد null حين لا يُسمع بأي إرسال.
    /// null تعني: لا تُستدعَ أي خدمة سحابية إطلاقاً.
    /// </summary>
    public CloudSharePayload? TryPrepare(string? question)
    {
        if (!_settings.CloudSharingEnabled)
        {
            return null;
        }

        var redacted = QuestionRedactor.Redact(question);
        if (!redacted.IsSafe || !RedactionValidator.IsSafe(redacted.Summary))
        {
            return null;
        }

        return new CloudSharePayload(
            redacted.Summary,
            redacted.RemovedCategories,
            AllowedPurpose);
    }
}
