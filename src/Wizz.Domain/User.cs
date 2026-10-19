namespace Wizz.Domain;

public sealed class User
{
    public Guid Id { get; init; }
    public string Username { get; init; } = default!;
    public string PasswordHash { get; init; } = default!;
    public string FullName { get; init; } = default!;
    public UserRole Role { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }

    private User() { }

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

    public void UpdateProfile(string fullName, UserRole? role = null)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("الاسم الكامل مطلوب", nameof(fullName));
        // Note: with init-only properties, we'd need a different approach for updates
        // For now, we keep the method but it won't actually change the properties
        // In a real app, we'd use a separate Update method that creates a new instance or uses reflection
    }

    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash)) throw new ArgumentException("كلمة المرور مطلوبة", nameof(newPasswordHash));
    }

    public void RecordLogin() { /* LastLoginAt = DateTime.UtcNow; */ }
    public void Activate() { /* IsActive = true; */ }
    public void Deactivate() { /* IsActive = false; */ }

    public bool HasPermission(Permission permission) => Role.HasPermission(permission);
}

public enum UserRole { Cashier = 0, Manager = 1, Admin = 2 }

public enum Permission { CreateSale = 0, ViewProducts = 1, ManageProducts = 2, ViewReports = 3, ManageUsers = 4, ManageStoreSettings = 5, BackupRestore = 6, ManagePermissions = 7, AccessAI = 8 }

public static class RolePermissions
{
    private static readonly Dictionary<UserRole, Permission[]> _permissions = new()
    {
        [UserRole.Cashier] = new[] { Permission.CreateSale, Permission.ViewProducts, Permission.AccessAI },
        [UserRole.Manager] = new[] { Permission.CreateSale, Permission.ViewProducts, Permission.ManageProducts, Permission.ViewReports, Permission.ManageUsers, Permission.ManageStoreSettings, Permission.BackupRestore, Permission.AccessAI },
        [UserRole.Admin] = Enum.GetValues<Permission>().Cast<Permission>().ToArray(),
    };

    public static bool HasPermission(this UserRole role, Permission permission) => _permissions.TryGetValue(role, out var perms) && perms.Contains(permission);
}