namespace Wizz.Presentation.Services;

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Wizz.Application.Security;
using Wizz.Domain;

/// <summary>
/// المستخدم الحالي لجلسة Blazor — يقرأ الهوية من <see cref="AuthenticationStateProvider"/>
/// بدل افتراض «أول مدير» أو تمرير معرّفات ثابتة.
/// </summary>
public sealed class CurrentUser
{
    private readonly AuthenticationStateProvider _provider;

    public CurrentUser(AuthenticationStateProvider provider) => _provider = provider;

    public async Task<ClaimsPrincipal> PrincipalAsync()
        => (await _provider.GetAuthenticationStateAsync()).User;

    public async Task<bool> IsAuthenticatedAsync()
        => (await PrincipalAsync()).Identity?.IsAuthenticated == true;

    public async Task<Guid?> IdAsync()
        => Guid.TryParse((await PrincipalAsync()).FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task<string?> UsernameAsync()
        => (await PrincipalAsync()).FindFirstValue(ClaimTypes.Name);

    public async Task<string?> FullNameAsync()
        => (await PrincipalAsync()).FindFirstValue(ClaimTypes.GivenName);

    public async Task<UserRole?> RoleAsync()
    {
        var value = (await PrincipalAsync()).FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(value, out var role) ? role : null;
    }

    public async Task<bool> HasPermissionAsync(Permission permission)
        => (await PrincipalAsync()).HasClaim(PermissionPolicy.ClaimType, permission.ToString());
}
