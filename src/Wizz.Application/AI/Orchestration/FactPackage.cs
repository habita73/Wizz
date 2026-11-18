namespace Wizz.Application.AI.Orchestration;

/// <summary>
/// «حزمة الحقائق» التي يُنتجها المدمج قبل أي تفويض: نصّ المدمج هو المرجع،
/// وقائمة الأرقام المستخرجة منه هي الرقم الوحيد المسموح بعرضه بعد الصياغة.
/// لا تُبنى الحزمة من توليد النص، بل من إجابة المدمج المشتقة من استعلامات محددة.
/// </summary>
/// <param name="AuthoritativeText">نص المدمج المرجعي.</param>
/// <param name="NumericTokens">الأرقام المقبولة بصيغة قياسية.</param>
/// <param name="Intent">نية المدمج (مبيعات/ديون/سعر/أدوات...).</param>
/// <param name="Subject">الموضوع المتذكَّر إن وُجد.</param>
public sealed record FactPackage(
    string AuthoritativeText,
    IReadOnlyList<string> NumericTokens,
    string? Intent,
    string? Subject)
{
    /// <summary>حزمة فارغة لا تسمح بأي رقم غير موثق.</summary>
    public static readonly FactPackage Empty =
        new(string.Empty, Array.Empty<string>(), null, null);

    /// <summary>هل تحتوي الحزمة على أرقام قابلة للتحكيم؟</summary>
    public bool HasNumbers => NumericTokens.Count > 0;

    /// <summary>تبني الحزمة من إجابة المدمج؛ غياب الجواب يعني حزمة فارغة.</summary>
    public static FactPackage FromBuiltIn(BuiltInAnswer? answer)
        => answer is null
            ? Empty
            : new FactPackage(
                answer.Text,
                NumberAdjudicator.ExtractCanonical(answer.Text),
                answer.Intent,
                answer.Subject);
}
