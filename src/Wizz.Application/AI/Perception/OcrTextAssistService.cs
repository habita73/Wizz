namespace Wizz.Application.AI.Perception;

using Wizz.Application.Abstractions;

/// <summary>نتيجة مساعدة النموذج المحلي: اقتراح للعرض فقط، أو سبب عدم التوفر.</summary>
/// <param name="Available">هل صدر اقتراح قابل للعرض؟</param>
/// <param name="Suggestion">النص المقترح؛ لا يُطبَّق تلقائيًا.</param>
/// <param name="Reason">سبب عدم التوفر، عند عدم التوفر فقط.</param>
public sealed record OcrTextAssistResult(bool Available, string Suggestion, string? Reason)
{
    public static OcrTextAssistResult Success(string suggestion) => new(true, suggestion, null);

    public static OcrTextAssistResult Unavailable(string reason) => new(false, string.Empty, reason);
}

/// <summary>
/// مساعدة اختيارية لتصحيح نص مستخرج محليًا، تعمل فقط عند استدعائها بعد ضغط المستخدم،
/// وبالموفر المحلي المُمرَّر إليها حصرًا. لا تختار مسارًا، لا ترسل للسحابة، لا تكتب في قاعدة البيانات،
/// ولا تُغيّر النص الأصلي؛ تُعيد اقتراحًا يعرضه المستخدم ويقرر هو وحده اعتماده.
/// </summary>
public sealed class OcrTextAssistService
{
    /// <summary>حد آمن لحجم النص المرسل للنموذج المحلي؛ يحمي الذاكرة والزمن.</summary>
    public const int MaxTextLength = 8000;

    private const string SystemInstruction =
        "أنت مصحّح نصوص وصول في متجر، تعمل محليًا. أعد كتابة النص المعطى بعد تصحيح الحروف العربية " +
        "والأرقام والكلمات المشوّهة فقط. حافظ على نفس عدد الأسطر وترتيبها، ولا تخترع منتجًا أو سعرًا " +
        "أو رقمًا لم يظهر في النص، ولا تحذف سطرًا، ولا تضف شرحًا أو عنوانًا. أعد النص المصحح فقط.";

    private readonly ILlmProvider _localProvider;

    public OcrTextAssistService(ILlmProvider localProvider)
    {
        _localProvider = localProvider ?? throw new ArgumentNullException(nameof(localProvider));
    }

    public async Task<OcrTextAssistResult> SuggestAsync(string rawText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return OcrTextAssistResult.Unavailable("لا يوجد نص لتصحيحه.");

        if (!await _localProvider.IsAvailableAsync(ct))
            return OcrTextAssistResult.Unavailable(
                "النموذج المحلي غير متاح؛ شغّل Ollama وثبّت نموذجًا محليًا، أو راجع النص يدويًا.");

        var text = rawText.Length > MaxTextLength ? rawText[..MaxTextLength] : rawText;

        try
        {
            var response = await _localProvider.CompleteWithContextAsync(
                SystemInstruction,
                text,
                ct);

            var suggestion = Clean(response);
            return string.IsNullOrWhiteSpace(suggestion)
                ? OcrTextAssistResult.Unavailable("لم يُصدر النموذج المحلي اقتراحًا؛ راجع النص يدويًا.")
                : OcrTextAssistResult.Success(suggestion);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OcrTextAssistResult.Unavailable("أُلغيت مساعدة النموذج المحلي.");
        }
        catch (Exception)
        {
            // لا يُسجَّل أي جزء من نص الوصل في أي سجل.
            return OcrTextAssistResult.Unavailable("تعذّت مساعدة النموذج المحلي؛ راجع النص يدويًا.");
        }
    }

    private static string Clean(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return string.Empty;

        var value = response.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = value.IndexOf('\n');
            if (firstBreak >= 0) value = value[(firstBreak + 1)..];
            var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) value = value[..lastFence];
        }

        return value.Trim();
    }
}
