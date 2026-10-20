using System.Security.Cryptography;

namespace Wizz.Application.Security;

/// <summary>
/// تجزئة كلمات المرور باستخدام PBKDF2-SHA256 مع ملح عشوائي لكل كلمة مرور.
/// الصيغة المخزنة: {iterations}.{saltBase64}.{hashBase64}
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;
    private const char Separator = '.';

    /// <summary>توليد تجزئة آمنة لكلمة المرور.</summary>
    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("كلمة المرور مطلوبة", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return string.Join(Separator, Iterations.ToString(), Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    /// <summary>التحقق من مطابقة كلمة المرور للتجزئة المخزنة.</summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;

        var parts = storedHash.Split(Separator);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
