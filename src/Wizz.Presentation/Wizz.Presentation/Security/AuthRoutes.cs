namespace Wizz.Presentation.Security;

/// <summary>ثوابت مسارات المصادقة المشتركة بين Program والصفحات.</summary>
public static class AuthRoutes
{
    public const string Login = "/login";
    public const string Logout = "/auth/logout";
    public const string Setup = "/setup";
    public const string AccessDenied = "/access-denied";
}
