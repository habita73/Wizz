namespace Wizz.Domain;

/// <summary>المتجر — بيانات النشاط التجاري والإعدادات المحلية.</summary>
public sealed class Store
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? TaxNumber { get; private set; }
    public string? CommercialRegister { get; private set; }
    public bool AllowCreditSales { get; private set; }
    public bool InventoryEnabled { get; private set; } = true;
    public string Currency { get; private set; } = "DZD";
    public string TimeZone { get; private set; } = "Africa/Algiers";
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Store() { } // EF Core

    public Store(
        string name,
        string? address = null,
        string? phone = null,
        string? taxNumber = null,
        string? commercialRegister = null,
        bool allowCreditSales = false,
        string currency = "DZD",
        bool inventoryEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المتجر مطلوب", nameof(name));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("العملة مطلوبة", nameof(currency));

        Id = Guid.NewGuid();
        Name = name.Trim();
        Address = Normalize(address);
        Phone = Normalize(phone);
        TaxNumber = Normalize(taxNumber);
        CommercialRegister = Normalize(commercialRegister);
        AllowCreditSales = allowCreditSales;
        InventoryEnabled = inventoryEnabled;
        Currency = currency.Trim().ToUpperInvariant();
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>تحديث إعدادات المتجر القابلة للتغيير.</summary>
    public void UpdateSettings(
        string name,
        string? address,
        string? phone,
        string? taxNumber,
        string? commercialRegister,
        bool allowCreditSales,
        string? currency = null,
        string? timeZone = null,
        bool inventoryEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المتجر مطلوب", nameof(name));

        Name = name.Trim();
        Address = Normalize(address);
        Phone = Normalize(phone);
        TaxNumber = Normalize(taxNumber);
        CommercialRegister = Normalize(commercialRegister);
        AllowCreditSales = allowCreditSales;
        InventoryEnabled = inventoryEnabled;

        if (!string.IsNullOrWhiteSpace(currency))
        {
            Currency = currency.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            TimeZone = timeZone.Trim();
        }

        UpdatedAt = DateTime.UtcNow;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
