namespace Wizz.Application.AI.Perception;

/// <summary>المسار الذي أنتج نتيجة التعرّف. التوزيع حسب القدرة، لا حسب ولاء المسار.</summary>
public enum PerceptionSource
{
    /// <summary>القواعد المدمجة على الجهاز.</summary>
    Embedded = 0,

    /// <summary>نموذج محلي على الجهاز (لا يرسل الصورة للخارج).</summary>
    Local = 1,

    /// <summary>مزود سحابي بعد تفعيل التشارك وتنقيح البيانات الشخصية.</summary>
    Cloud = 2
}

/// <summary>
/// مرشّح نتيجة تعرّف من صورة أو مستند. يبقى «مرشّحًا» حتى يُحكّمه المدمج؛
/// لا يُستخدم مباشرة في أي حساب أو عرض.
/// </summary>
/// <param name="ProductName">اسم المنتج المقترح من النص المستخرج (قد يكون فارغاً).</param>
/// <param name="Price">السعر المقترح (قد يكون فارغاً).</param>
/// <param name="RawLine">السطر الخام المستخرج من الصورة — للعرض التشخيصي فقط.</param>
/// <param name="Confidence">ثقة أولية بسيطة من اكتمال العناصر (0…1)، لا تُعد تحكيمًا.</param>
/// <param name="Source">المسار الذي أنتج المرشّح.</param>
public sealed record PerceptionCandidate(
    string? ProductName,
    decimal? Price,
    string RawLine,
    double Confidence,
    PerceptionSource Source);

/// <summary>حكم المدمج على مرشّح واحد.</summary>
/// <param name="Candidate">المرشّح الأصلي.</param>
/// <param name="Accepted">هل اجتاز تحكيم المدمج؟</param>
/// <param name="Reason">سبب مفهوم يظهر للمستخدم.</param>
public sealed record AdjudicatedCandidate(
    PerceptionCandidate Candidate,
    bool Accepted,
    string Reason);

/// <summary>
/// نتيجة تحكيم المدمج على دفعة من مرشّحات التعرّف. الحقل <see cref="WrittenToDatabase"/>
/// يبقى <c>false</c> دائمًا في هذه المرحلة: لا كتابة تلقائية في قاعدة البيانات.
/// </summary>
public sealed record PerceptionResult(
    IReadOnlyList<AdjudicatedCandidate> Candidates,
    int AcceptedCount,
    int RejectedCount,
    string Summary,
    bool WrittenToDatabase = false)
{
    /// <summary>
    /// قراءات بديلة للصفوف المكتظة لم يُحكَم عليها؛ للمراجعة فقط، ولا تُعدّ أصنافًا ولا تدخل العدد.
    /// </summary>
    public IReadOnlyList<PerceptionCandidate> PossibleReadings { get; init; } = Array.Empty<PerceptionCandidate>();
}
