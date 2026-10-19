namespace Wizz.Application.DTOs;

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
    UnitOfMeasureDto? BaseUnit
);

/// <summary>بيانات إنشاء/تحديث منتج.</summary>
public sealed record ProductInputDto(
    string Name,
    decimal RetailPrice,
    decimal WholesalePrice,
    bool IsService,
    string? Barcode,
    string? Description,
    IReadOnlyList<UnitInputDto>? Units
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