namespace Wizz.Domain;

/// <summary>
/// سجل تدقيق للإجراءات الحساسة — من فعل ماذا ومتى وما النتيجة.
/// لا يحتوي كلمات مرور ولا أسرارًا؛ الحقول النصية قصيرة ومقصّة.
/// </summary>
public sealed class AuditLog
{
    public const int MaxDetailsLength = 1000;

    public Guid Id { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Username { get; private set; }
    public AuditAction Action { get; private set; }
    public AuditOutcome Outcome { get; private set; }
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? Details { get; private set; }
    public string? IpAddress { get; private set; }

    private AuditLog() { } // EF Core

    public AuditLog(
        AuditAction action,
        AuditOutcome outcome,
        Guid? userId = null,
        string? username = null,
        string? entityType = null,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null,
        DateTime? occurredAtUtc = null)
    {
        Id = Guid.NewGuid();
        Action = action;
        Outcome = outcome;
        UserId = userId;
        Username = Truncate(username, 100);
        EntityType = Truncate(entityType, 100);
        EntityId = Truncate(entityId, 100);
        Details = Truncate(details, MaxDetailsLength);
        IpAddress = Truncate(ipAddress, 64);
        OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>أنواع الإجراءات المسجلة في سجل التدقيق.</summary>
public enum AuditAction
{
    LoginSucceeded = 0,
    LoginFailed = 1,
    Logout = 2,
    FirstRunSetup = 3,
    UserCreated = 4,
    UserUpdated = 5,
    UserPasswordReset = 6,
    UserActivated = 7,
    UserDeactivated = 8,
    BackupCreated = 9,
    BackupRestored = 10,
    BackupDeleted = 11,
    StoreSettingsUpdated = 12,
    AiSettingsUpdated = 13,
    SaleCreated = 14,
    SaleCancelled = 15,
    SaleModified = 16,
    StockAdjusted = 17,
    ProductCreated = 18,
    ProductUpdated = 19,
    ProductDeleted = 20,
    DefaultCredentialDisabled = 21,
    PermissionDenied = 22,
    ShiftOpened = 23,
    ShiftClosed = 24,
    CashMovementRecorded = 25,
    PurchaseCreated = 26,
    PurchaseVoided = 27,
    CashDifferenceRecorded = 28,
}

/// <summary>نتيجة الإجراء المسجل.</summary>
public enum AuditOutcome
{
    Success = 0,
    Failure = 1,
    Denied = 2,
}
