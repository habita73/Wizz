namespace Wizz.Application.DTOs;

/// <summary>مستخدم للعرض.</summary>
public sealed record UserDto(
    Guid Id,
    string Username,
    string FullName,
    UserRoleDto Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt
);

/// <summary>إدخال إنشاء مستخدم.</summary>
public sealed record CreateUserInputDto(
    string Username,
    string Password,
    string FullName,
    UserRoleDto Role
);

/// <summary>إدخال تحديث مستخدم.</summary>
public sealed record UpdateUserInputDto(
    string FullName,
    UserRoleDto? Role,
    bool? IsActive
);

/// <summary>إدخال تغيير كلمة المرور.</summary>
public sealed record ChangePasswordInputDto(
    string CurrentPassword,
    string NewPassword
);

public enum UserRoleDto { Cashier = 0, Manager = 1, Admin = 2 }