namespace Wizz.Domain;

/// <summary>تسجيل جهاز — للاستعداد للمزامنة متعددة الأجهزة (واجهة فقط في MVP).</summary>
public sealed class DeviceRegistration
{
    public Guid Id { get; private set; }
    public string DeviceId { get; private set; } = default!;      // معرف فريد للجهاز
    public string DeviceName { get; private set; } = default!;    // اسم ودّي
    public string DeviceType { get; private set; } = default!;    // "Desktop", "Mobile", "Tablet"
    public string? PublicKey { get; private set; }                // مفتاح عام للتشفير/التوقيع
    public DeviceStatus Status { get; private set; }
    public DateTime RegisteredAt { get; private set; }
    public DateTime? LastSeenAt { get; private set; }
    public Guid? StoreId { get; private set; }

    private DeviceRegistration() { }

    public DeviceRegistration(string deviceId, string deviceName, string deviceType, string? publicKey = null, Guid? storeId = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("معرف الجهاز مطلوب", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("اسم الجهاز مطلوب", nameof(deviceName));

        Id = Guid.NewGuid();
        DeviceId = deviceId.Trim();
        DeviceName = deviceName.Trim();
        DeviceType = deviceType.Trim();
        PublicKey = string.IsNullOrWhiteSpace(publicKey) ? null : publicKey.Trim();
        Status = DeviceStatus.Pending;
        RegisteredAt = DateTime.UtcNow;
        StoreId = storeId;
    }

    public void Approve() => Status = DeviceStatus.Approved;
    public void Reject() => Status = DeviceStatus.Rejected;
    public void Revoke() => Status = DeviceStatus.Revoked;
    public void RecordSeen() => LastSeenAt = DateTime.UtcNow;
}

public enum DeviceStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Revoked = 3
}