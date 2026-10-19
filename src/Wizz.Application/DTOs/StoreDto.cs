namespace Wizz.Application.DTOs;

/// <summary>إعدادات المتجر.</summary>
public sealed record StoreDto(
    Guid Id,
    string Name,
    string? Address,
    string? Phone,
    string? TaxNumber,
    string? CommercialRegister,
    bool AllowCreditSales,
    string Currency,
    string TimeZone
);

/// <summary>إدخال إعدادات المتجر.</summary>
public sealed record StoreInputDto(
    string Name,
    string? Address,
    string? Phone,
    string? TaxNumber,
    string? CommercialRegister,
    bool AllowCreditSales
);