namespace Wizz.Domain;

/// <summary>اشتراك/باقة — للاستعداد للنموذج التجاري (واجهة فقط في MVP).</summary>
public sealed class Subscription
{
    public Guid Id { get; private set; }
    public string PlanName { get; private set; } = default!;        // "Free", "Pro", "Enterprise"
    public string Provider { get; private set; } = default!;        // "Local", "Stripe", "LocalGateway"
    public string? ExternalId { get; private set; }                 // معرف خارجي (Stripe subscription ID, etc.)
    public SubscriptionStatus Status { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public decimal? MonthlyPrice { get; private set; }
    public string Currency { get; private set; } = "DZD";
    public Guid StoreId { get; private set; }

    private Subscription() { }

    public Subscription(string planName, string provider, Guid storeId, string? externalId = null, DateTime? expiresAt = null, decimal? monthlyPrice = null)
    {
        if (string.IsNullOrWhiteSpace(planName)) throw new ArgumentException("اسم الباقة مطلوب", nameof(planName));
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("المزود مطلوب", nameof(provider));

        Id = Guid.NewGuid();
        PlanName = planName.Trim();
        Provider = provider.Trim();
        ExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim();
        StoreId = storeId;
        Status = SubscriptionStatus.Active;
        StartedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
        MonthlyPrice = monthlyPrice;
    }

    public void Renew(DateTime newExpiresAt) { ExpiresAt = newExpiresAt; Status = SubscriptionStatus.Active; }
    public void Cancel() { Status = SubscriptionStatus.Cancelled; CancelledAt = DateTime.UtcNow; }
    public void Expire() => Status = SubscriptionStatus.Expired;
    public void Suspend() => Status = SubscriptionStatus.Suspended;
}

public enum SubscriptionStatus
{
    Active = 0,
    Expired = 1,
    Cancelled = 2,
    Suspended = 3,
    Trial = 4
}