namespace Wizz.Domain;

/// <summary>منتج/خدمة — يدعم سعر تجزئة وسعر جملة، وحدات متعددة مع تحويل، وباركود.</summary>
public sealed class Product
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Barcode { get; private set; }
    public string? Description { get; private set; }
    public decimal RetailPrice { get; private set; }      // سعر التجزئة
    public decimal WholesalePrice { get; private set; }   // سعر الجملة
    public bool IsService { get; private set; }           // خدمة (لا مخزون) أم منتج
    public decimal StockQuantity { get; private set; }    // الكمية المتاحة في المخزون يدوياً
    public decimal ReorderLevel { get; private set; }     // حد إعادة الطلب — التنبيه عند بلوغه
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // وحدات القياس
    private readonly List<UnitOfMeasure> _units = new();
    public IReadOnlyList<UnitOfMeasure> Units => _units.AsReadOnly();

    private Product() { } // EF Core

    public Product(string name, decimal retailPrice, decimal wholesalePrice, bool isService = false, string? barcode = null, string? description = null,
        decimal stockQuantity = 0m, decimal reorderLevel = 0m)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المنتج مطلوب", nameof(name));
        Money.RequireAtMostTwoDecimalPlaces(retailPrice, nameof(retailPrice));
        Money.RequireAtMostTwoDecimalPlaces(wholesalePrice, nameof(wholesalePrice));
        if (retailPrice < 0) throw new ArgumentOutOfRangeException(nameof(retailPrice), "سعر التجزئة لا يمكن أن يكون سالباً");
        if (wholesalePrice < 0) throw new ArgumentOutOfRangeException(nameof(wholesalePrice), "سعر الجملة لا يمكن أن يكون سالباً");
        if (stockQuantity < 0) throw new ArgumentOutOfRangeException(nameof(stockQuantity), "كمية المخزون لا يمكن أن تكون سالبة");
        if (reorderLevel < 0) throw new ArgumentOutOfRangeException(nameof(reorderLevel), "حد إعادة الطلب لا يمكن أن يكون سالباً");

        Id = Guid.NewGuid();
        Name = name.Trim();
        RetailPrice = retailPrice;
        WholesalePrice = wholesalePrice;
        IsService = isService;
        StockQuantity = isService ? 0m : stockQuantity;
        ReorderLevel = isService ? 0m : reorderLevel;
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(string name, decimal retailPrice, decimal wholesalePrice, bool isActive, bool isService, string? barcode = null, string? description = null,
        decimal stockQuantity = 0m, decimal reorderLevel = 0m)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المنتج مطلوب", nameof(name));
        Money.RequireAtMostTwoDecimalPlaces(retailPrice, nameof(retailPrice));
        Money.RequireAtMostTwoDecimalPlaces(wholesalePrice, nameof(wholesalePrice));
        if (retailPrice < 0) throw new ArgumentOutOfRangeException(nameof(retailPrice), "سعر التجزئة لا يمكن أن يكون سالباً");
        if (wholesalePrice < 0) throw new ArgumentOutOfRangeException(nameof(wholesalePrice), "سعر الجملة لا يمكن أن يكون سالباً");
        if (stockQuantity < 0) throw new ArgumentOutOfRangeException(nameof(stockQuantity), "كمية المخزون لا يمكن أن تكون سالبة");
        if (reorderLevel < 0) throw new ArgumentOutOfRangeException(nameof(reorderLevel), "حد إعادة الطلب لا يمكن أن يكون سالباً");

        Name = name.Trim();
        RetailPrice = retailPrice;
        WholesalePrice = wholesalePrice;
        IsActive = isActive;
        IsService = isService;
        StockQuantity = isService ? 0m : stockQuantity;
        ReorderLevel = isService ? 0m : reorderLevel;
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>تعديل سريع لكمية المخزون وحد التنبيه دون تغيير بيانات المنتج الأخرى.</summary>
    public void SetStock(decimal quantity, decimal reorderLevel)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity), "كمية المخزون لا يمكن أن تكون سالبة");
        if (reorderLevel < 0) throw new ArgumentOutOfRangeException(nameof(reorderLevel), "حد إعادة الطلب لا يمكن أن يكون سالباً");

        StockQuantity = IsService ? 0m : quantity;
        ReorderLevel = IsService ? 0m : reorderLevel;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>تنبيه المخزون: منتج (لا خدمة) بلغ أو تجاوز حد إعادة الطلب.</summary>
    public bool IsLowStock => !IsService && ReorderLevel > 0 && StockQuantity <= ReorderLevel;

    /// <summary>إضافة وحدة قياس للمنتج. أول وحدة تضاف تصبح الوحدة الأساسية تلقائياً.</summary>
    public UnitOfMeasure AddUnit(string name, string symbol, decimal baseQuantity)
    {
        var isBase = _units.Count == 0;
        var unit = new UnitOfMeasure(name, symbol, baseQuantity, isBase);
        unit.AttachToProduct(Id);
        _units.Add(unit);
        return unit;
    }

    public void RemoveUnit(Guid unitId)
    {
        var unit = _units.FirstOrDefault(u => u.Id == unitId);
        if (unit == null) throw new ArgumentException("وحدة غير موجودة", nameof(unitId));
        if (_units.Count == 1) throw new InvalidOperationException("لا يمكن حذف الوحدة الوحيدة للمنتج");
        if (unit.IsBaseUnit)
        {
            _units.First(u => u.Id != unitId).SetAsBaseUnit();
        }
        _units.Remove(unit);
    }

    public UnitOfMeasure? GetBaseUnit() => _units.FirstOrDefault(u => u.IsBaseUnit);

    public UnitOfMeasure? GetUnitById(Guid unitId) => _units.FirstOrDefault(u => u.Id == unitId);

    public UnitOfMeasure? GetUnitByName(string name) => _units.FirstOrDefault(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>الحصول على السعر حسب نوع البيع (تجزئة/جملة).</summary>
    public decimal GetPrice(bool isWholesale) => isWholesale ? WholesalePrice : RetailPrice;

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}