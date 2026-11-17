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

    /// <summary>تفعيل وحدة الصندوق وعدّ النقدية. تعطيلها يعيد إغلاق الوردية إلى سلوكه السابق.</summary>
    public bool CashDrawerEnabled { get; private set; } = true;

    /// <summary>حدّ فرق الإغلاق الذي لا يتطلب ملاحظة (منزلتان عشريتان، افتراضي صفر).</summary>
    public decimal CashDifferenceTolerance { get; private set; }

    /// <summary>عدد أرقام السنة في رقم المستند: 2 أو 4 (الافتراضي 4).</summary>
    public int DocumentYearDigits { get; private set; } = 4;

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
        bool inventoryEnabled = true,
        int documentYearDigits = 4,
        bool cashDrawerEnabled = true,
        decimal cashDifferenceTolerance = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المتجر مطلوب", nameof(name));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("العملة مطلوبة", nameof(currency));
        if (cashDifferenceTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(cashDifferenceTolerance), "حدّ فرق الصندوق لا يمكن أن يكون سالبًا");
        Money.RequireAtMostTwoDecimalPlaces(cashDifferenceTolerance, nameof(cashDifferenceTolerance));
        RequireValidYearDigits(documentYearDigits, nameof(documentYearDigits));

        Id = Guid.NewGuid();
        Name = name.Trim();
        Address = Normalize(address);
        Phone = Normalize(phone);
        TaxNumber = Normalize(taxNumber);
        CommercialRegister = Normalize(commercialRegister);
        AllowCreditSales = allowCreditSales;
        InventoryEnabled = inventoryEnabled;
        DocumentYearDigits = documentYearDigits;
        CashDrawerEnabled = cashDrawerEnabled;
        CashDifferenceTolerance = Money.Round(cashDifferenceTolerance);
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
        bool inventoryEnabled = true,
        int? documentYearDigits = null,
        bool? cashDrawerEnabled = null,
        decimal? cashDifferenceTolerance = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المتجر مطلوب", nameof(name));
        if (documentYearDigits.HasValue)
            RequireValidYearDigits(documentYearDigits.Value, nameof(documentYearDigits));
        if (cashDifferenceTolerance.HasValue)
        {
            if (cashDifferenceTolerance.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(cashDifferenceTolerance), "حدّ فرق الصندوق لا يمكن أن يكون سالبًا");
            Money.RequireAtMostTwoDecimalPlaces(cashDifferenceTolerance.Value, nameof(cashDifferenceTolerance));
        }

        Name = name.Trim();
        Address = Normalize(address);
        Phone = Normalize(phone);
        TaxNumber = Normalize(taxNumber);
        CommercialRegister = Normalize(commercialRegister);
        AllowCreditSales = allowCreditSales;
        InventoryEnabled = inventoryEnabled;
        if (documentYearDigits.HasValue)
            DocumentYearDigits = documentYearDigits.Value;
        if (cashDrawerEnabled.HasValue)
            CashDrawerEnabled = cashDrawerEnabled.Value;
        if (cashDifferenceTolerance.HasValue)
            CashDifferenceTolerance = Money.Round(cashDifferenceTolerance.Value);

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

    private static void RequireValidYearDigits(int value, string paramName)
    {
        if (value is not (2 or 4))
            throw new ArgumentOutOfRangeException(paramName, "عدد أرقام السنة يجب أن يكون 2 أو 4");
    }
}
