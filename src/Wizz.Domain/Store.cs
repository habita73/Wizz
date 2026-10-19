namespace Wizz.Domain;

public sealed class Store
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? TaxNumber { get; init; }
    public string? CommercialRegister { get; init; }
    public bool AllowCreditSales { get; init; }
    public string Currency { get; init; } = "DZD";
    public string TimeZone { get; init; } = "Africa/Algiers";
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    private Store() { }

    public Store(string name, string? address = null, string? phone = null, string? taxNumber = null, string? commercialRegister = null, bool allowCreditSales = false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المتجر مطلوب", nameof(name));

        Id = Guid.NewGuid();
        Name = name.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        TaxNumber = string.IsNullOrWhiteSpace(taxNumber) ? null : taxNumber.Trim();
        CommercialRegister = string.IsNullOrWhiteSpace(commercialRegister) ? null : commercialRegister.Trim();
        AllowCreditSales = allowCreditSales;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateSettings(string name, string? address, string? phone, string? taxNumber, string? commercialRegister, bool allowCreditSales)
    {
        // With init-only properties, updates would need a different approach
    }
}