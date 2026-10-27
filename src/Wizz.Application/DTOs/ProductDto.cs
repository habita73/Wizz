namespace Wizz.Application.DTOs;

using Wizz.Domain;

/// <summary>وحدة قياس للمنتج — للعرض والنقل.</summary>
public sealed record UnitOfMeasureDto(
    Guid Id,
    string Name,
    string Symbol,
    decimal BaseQuantity,
    bool IsBaseUnit
);

/// <summary>منتج للعرض والقوائم.</summary>
public sealed record ProductDto(
    Guid Id,
    string Name,
    string? Barcode,
    string? Description,
    decimal RetailPrice,
    decimal WholesalePrice,
    bool IsService,
    bool IsActive,
    IReadOnlyList<UnitOfMeasureDto> Units,
    UnitOfMeasureDto? BaseUnit,
    decimal StockQuantity = 0m,
    decimal ReorderLevel = 0m
)
{
    /// <summary>منتج (لا خدمة) بلغ أو تجاوز حد إعادة الطلب.</summary>
    public bool IsLowStock => !IsService && (StockQuantity < 0 || (ReorderLevel > 0 && StockQuantity <= ReorderLevel));

    /// <summary>نفد رصيد المنتج.</summary>
    public bool IsOutOfStock => !IsService && StockQuantity <= 0;

    /// <summary>تحويل كيان المنتج إلى DTO للعرض.</summary>
    public static ProductDto FromDomain(Product p) => new(
        p.Id, p.Name, p.Barcode, p.Description, p.RetailPrice, p.WholesalePrice,
        p.IsService, p.IsActive,
        p.Units.Select(u => new UnitOfMeasureDto(u.Id, u.Name, u.Symbol, u.BaseQuantity, u.IsBaseUnit)).ToList(),
        p.GetBaseUnit() is { } bu ? new UnitOfMeasureDto(bu.Id, bu.Name, bu.Symbol, bu.BaseQuantity, true) : null,
        p.StockQuantity, p.ReorderLevel);
}

/// <summary>بيانات إنشاء/تحديث منتج.</summary>
public sealed record ProductInputDto(
    string Name,
    decimal RetailPrice,
    decimal WholesalePrice,
    bool IsService,
    string? Barcode,
    string? Description,
    IReadOnlyList<UnitInputDto>? Units,
    decimal StockQuantity = 0m,
    decimal ReorderLevel = 0m
);

/// <summary>وحدة إدخال.</summary>
public sealed record UnitInputDto(
    string Name,
    string Symbol,
    decimal BaseQuantity
);

/// <summary>نتيجة بحث منتج.</summary>
public sealed record ProductSearchResultDto(
    IReadOnlyList<ProductDto> Products,
    int TotalCount,
    int Page,
    int PageSize
);