namespace Wizz.Domain;

using System.Text;

/// <summary>
/// قواعد توحيد اسم الزبون عند البحث والاقتراح. لا تُغيّر النص المخزَّن، بل تُنتج
/// مفتاحًا قياسيًا يجعل «أمين» و«امين» و«آمين» اسمًا واحدًا عند عرض الاقتراحات،
/// حتى لا يتكرر الشخص نفسه بهجاءات مختلفة في سجل الديون.
/// </summary>
public static class CustomerNameRules
{
    /// <summary>
    /// مفتاح المقارنة: توحيد الألف والهمزات والتاء المربوطة والألف المقصورة،
    /// وإزالة التشكيل والتطويل، وتوحيد المسافات والحالة اللاتينية.
    /// </summary>
    public static string Key(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var buffer = new StringBuilder(value.Length);
        foreach (var raw in value.Trim())
        {
            // التشكيل والتطويل والمدّ لا تُفرّق بين هجاء وآخر لاسم واحد.
            if (raw == '\u0640' || raw == '\u0670' || (raw >= '\u064B' && raw <= '\u065F'))
                continue;

            var c = raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ؤ' => 'و',
                'ئ' or 'ى' => 'ي',
                'ة' => 'ه',
                _ => raw
            };

            buffer.Append(char.ToLowerInvariant(c));
        }

        return string.Join(' ', buffer.ToString()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>هل الاسمان نفس الشخص وفق قواعد التوحيد؟</summary>
    public static bool IsSame(string? first, string? second)
    {
        var key = Key(first);
        return key.Length > 0 && key == Key(second);
    }
}
