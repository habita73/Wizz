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

    /// <summary>
    /// بصمة أمان تُضمَّن في كوكي الدخول وتُتحقق عند كل طلب. تتغير عند تعديل كلمة المرور
    /// أو الدور أو تعطيل الحساب، فتُبطل الجلسات القديمة فورًا.
    /// </summary>
    public string SecurityStamp { get; private set; } = default!;

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
        SecurityStamp = NewSecurityStamp();
    }

    /// <summary>تحديث الاسم الكامل والدور. تغيير الدور يُبطل الجلسات القائمة.</summary>
    public void UpdateProfile(string fullName, UserRole? role = null)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("الاسم الكامل مطلوب", nameof(fullName));

        FullName = fullName.Trim();
        if (role.HasValue && role.Value != Role)
        {
            Role = role.Value;
            RotateSecurityStamp();
        }
    }

    /// <summary>تغيير كلمة المرور (يُمرَّر التجزئة الجاهزة) وإبطال الجلسات القائمة.</summary>
    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash)) throw new ArgumentException("كلمة المرور مطلوبة", nameof(newPasswordHash));
        PasswordHash = newPasswordHash;
        RotateSecurityStamp();
    }

    /// <summary>إبطال كل جلسات هذا المستخدم دون تغيير كلمة المرور أو الدور.</summary>
    public void RotateSecurityStamp() => SecurityStamp = NewSecurityStamp();

    public void RecordLogin() => LastLoginAt = DateTime.UtcNow;

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        IsActive = false;
        RotateSecurityStamp();
    }

    public bool HasPermission(Permission permission) => Role.HasPermission(permission);

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
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
    CancelSale = 9,
    ModifySale = 10,
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
            Permission.ManageStoreSettings,
            Permission.AccessAI,
            Permission.CancelSale,
            Permission.ModifySale,
        },
        [UserRole.Admin] = Enum.GetValues<Permission>(),
    };

    public static bool HasPermission(this UserRole role, Permission permission) =>
        PermissionsByRole.TryGetValue(role, out var permissions) && permissions.Contains(permission);
}
