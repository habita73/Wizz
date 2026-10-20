namespace Wizz.Domain;

/// <summary>مستخدم النظام — كاشير أو مدير أو مدير نظام.</summary>
public sealed class User
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }

    private User() { } // EF Core

    public User(string username, string passwordHash, string fullName, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("اسم المستخدم مطلوب", nameof(username));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("كلمة المرور مطلوبة", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("الاسم الكامل مطلوب", nameof(fullName));

        Id = Guid.NewGuid();
        Username = username.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        FullName = fullName.Trim();
        Role = role;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>تحديث الاسم الكامل والدور.</summary>
    public void UpdateProfile(string fullName, UserRole? role = null)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("الاسم الكامل مطلوب", nameof(fullName));

        FullName = fullName.Trim();
        if (role.HasValue)
        {
            Role = role.Value;
        }
    }

    /// <summary>تغيير كلمة المرور (يُمرَّر التجزئة الجاهزة).</summary>
    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash)) throw new ArgumentException("كلمة المرور مطلوبة", nameof(newPasswordHash));
        PasswordHash = newPasswordHash;
    }

    public void RecordLogin() => LastLoginAt = DateTime.UtcNow;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool HasPermission(Permission permission) => Role.HasPermission(permission);
}

public enum UserRole { Cashier = 0, Manager = 1, Admin = 2 }

public enum Permission
{
    CreateSale = 0,
    ViewProducts = 1,
    ManageProducts = 2,
    ViewReports = 3,
    ManageUsers = 4,
    ManageStoreSettings = 5,
    BackupRestore = 6,
    ManagePermissions = 7,
    AccessAI = 8,
}

public static class RolePermissions
{
    private static readonly Dictionary<UserRole, Permission[]> PermissionsByRole = new()
    {
        [UserRole.Cashier] = new[] { Permission.CreateSale, Permission.ViewProducts, Permission.AccessAI },
        [UserRole.Manager] = new[]
        {
            Permission.CreateSale,
            Permission.ViewProducts,
            Permission.ManageProducts,
            Permission.ViewReports,
            Permission.ManageUsers,
            Permission.ManageStoreSettings,
            Permission.BackupRestore,
            Permission.AccessAI,
        },
        [UserRole.Admin] = Enum.GetValues<Permission>(),
    };

    public static bool HasPermission(this UserRole role, Permission permission) =>
        PermissionsByRole.TryGetValue(role, out var permissions) && permissions.Contains(permission);
}
