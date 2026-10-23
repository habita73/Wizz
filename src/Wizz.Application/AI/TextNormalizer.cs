namespace Wizz.Application.AI;

using System.Text;

/// <summary>
/// تطبيع النصوص العربية لمطابقة أسئلة المستخدم بالبيانات المحلية:
/// توحيد الهمزات والياء والتاء المربوطة، تحويل الأرقام العربية/الفارسية، وحذف التشكيل.
/// </summary>
public static class TextNormalizer
{
    /// <summary>يعيد النص مطبَّعاً (أو نصاً فارغاً إن كان الإدخال فارغاً).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.Trim().ToLowerInvariant())
        {
            // توحيد الأرقام العربية والفارسية
            if (raw is >= '\u0660' and <= '\u0669')
            {
                sb.Append((char)('0' + (raw - '\u0660')));
                continue;
            }

            if (raw is >= '\u06F0' and <= '\u06F9')
            {
                sb.Append((char)('0' + (raw - '\u06F0')));
                continue;
            }

            sb.Append(raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                'ة' => 'ه',
                '\u064B' or '\u064C' or '\u064D' or '\u064E' or '\u064F' or
                '\u0650' or '\u0651' or '\u0652' or '\u0640' => '\0',
                _ => raw
            });
        }

        return sb.Replace("\0", string.Empty).ToString();
    }

    /// <summary>هل يحتوي النص على أي كلمة من الكلمات المعطاة؟</summary>
    public static bool HasAny(string? text, params string[] keywords)
        => !string.IsNullOrEmpty(text)
           && keywords.Any(k => !string.IsNullOrEmpty(k) && text.Contains(k, StringComparison.Ordinal));
}
