namespace Wizz.Application.AI;

/// <summary>
/// مرادفات «النواة المشتركة» للهجة الجزائرية في المحادثة فقط.
/// تغطّي كلمات مفهومة على نطاق واسع، ولا تتضمن قواميس مناطقية (تُضاف عند الحاجة بقرار منفصل).
/// لا تُستخدم هذه المرادفات في نصوص الفواتير أو المستندات الملزمة.
/// </summary>
public static class DialectSynonyms
{
    /// <summary>مرادفات «المنتجات / البضاعة» (بصيغة مُطبَّعة).</summary>
    public static readonly string[] Goods = { "البضاعه", "البضايع" };

    /// <summary>مرادفات «اعرض / أرني» (بصيغة مُطبَّعة).</summary>
    public static readonly string[] Show = { "وريني", "دلني", "لقي" };

    /// <summary>مرادفات «كم / بكم / السعر» المستخدمة في سؤال السعر.</summary>
    public static readonly string[] Price = { "شحال", "بشحال", "التمن" };
}
