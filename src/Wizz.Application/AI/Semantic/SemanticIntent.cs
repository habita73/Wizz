namespace Wizz.Application.AI.Semantic;

/// <summary>النية الدلالية المستخرجة من سؤال المستخدم للاستخدام المحلي فقط.</summary>
public enum SemanticIntent
{
    /// <summary>سؤال عام لا يتطلب بيانات المتجر.</summary>
    General = 0,

    /// <summary>مبيعات وإيرادات.</summary>
    Sales = 1,

    /// <summary>ديون ومستحقات.</summary>
    Debt = 2,

    /// <summary>سعر منتج.</summary>
    Price = 3,

    /// <summary>منتجات ومخزون.</summary>
    Product = 4,

    /// <summary>تحليل أو مقارنة أو متوسط.</summary>
    Analysis = 5,

    /// <summary>أدوات النظام (الوقت، الملفات، الطباعة...).</summary>
    Tools = 6
}

/// <summary>فهم دلالي لسؤال واحد.</summary>
/// <param name="Intent">النية المستخرجة.</param>
/// <param name="IsFollowUp">هل السؤال يعتمد على موضوع سابق؟</param>
/// <param name="Subject">الموضوع المستخرج من السؤال إن وُجد.</param>
/// <param name="ResolvedSubject">الموضوع بعد حل المرجع من الذاكرة (قد يساوي <paramref name="Subject"/>).</param>
/// <param name="MatchedMarkers">العلامات التي أدّت إلى القرار — للشفافية والتشخيص.</param>
public sealed record SemanticUnderstanding(
    SemanticIntent Intent,
    bool IsFollowUp,
    string? Subject,
    string? ResolvedSubject,
    IReadOnlyList<string> MatchedMarkers);
