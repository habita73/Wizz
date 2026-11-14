namespace Wizz.Presentation.Security;

using System.Security.Claims;
using Wizz.Application.Security;
using Wizz.Domain;

/// <summary>بناء هوية المستخدم ومطالبات الصلاحيات مرة واحدة عند تسجيل الدخول.</summary>
public static class UserClaimsFactory
{
    public const string SecurityStampClaimType = "wizz:security_stamp";

    public static ClaimsPrincipal Create(User user, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(SecurityStampClaimType, user.SecurityStamp),
        };

        foreach (var permission in Enum.GetValues<Permission>())
        {
            if (user.HasPermission(permission))
                claims.Add(new Claim(PermissionPolicy.ClaimType, permission.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }
}
