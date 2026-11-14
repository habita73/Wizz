namespace Wizz.Application.Security;

/// <summary>سياسة كلمة المرور الدنيا — تُطبَّق عند الإنشاء وإعادة التعيين.</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    /// <summary>تُرجع قائمة أخطاء عربية فارغة إذا كانت كلمة المرور مقبولة.</summary>
    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("كلمة المرور مطلوبة");
            return errors;
        }

        if (password.Length < MinimumLength)
            errors.Add($"كلمة المرور يجب أن تكون {MinimumLength} أحرف على الأقل");

        if (!password.Any(char.IsLetter))
            errors.Add("كلمة المرور يجب أن تحتوي حرفًا واحدًا على الأقل");

        if (!password.Any(char.IsDigit))
            errors.Add("كلمة المرور يجب أن تحتوي رقمًا واحدًا على الأقل");

        return errors;
    }

    public static bool IsValid(string? password) => Validate(password).Count == 0;
}
