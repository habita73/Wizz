namespace Wizz.Application.DTOs;

/// <summary>سطر بيع للعرض.</summary>
public sealed record SaleLineDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? ProductBarcode,
    Guid UnitId,
    string UnitName,
    string UnitSymbol,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    bool IsWholesalePrice
);

/// <summary>عملية بيع للعرض والتقارير.</summary>
public sealed record SaleDto(
    Guid Id,
    DateTime SaleDate,
    SaleStatusDto Status,
    IReadOnlyList<SaleLineDto> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal Paid,
    decimal Remaining,
    PaymentMethodDto PaymentMethod,
    bool IsCreditSale,
    DateTime? DueDate,
    Guid? CashierId,
    string? CashierName,
    bool IsModified,
    DateTime? ModifiedAt,
    Guid? ModifiedByUserId,
    string? ModificationNote
);

/// <summary>إدخال سطر بيع جديد.</summary>
public sealed record SaleLineInputDto(
    Guid ProductId,
    Guid UnitId,
    decimal Quantity,
    bool UseWholesalePrice
);

/// <summary>إدخال عملية بيع جديدة.</summary>
public sealed record SaleInputDto(
    IReadOnlyList<SaleLineInputDto> Lines,
    decimal Discount,
    decimal Paid,
    PaymentMethodDto PaymentMethod,
    bool IsCreditSale,
    DateTime? DueDate
);

/// <summary>ملخص بيع للقوائم والتقارير.</summary>
public sealed record SaleSummaryDto(
    Guid Id,
    DateTime SaleDate,
    decimal Total,
    decimal Paid,
    decimal Remaining,
    PaymentMethodDto PaymentMethod,
    bool IsCreditSale,
    string? CashierName
);

public enum SaleStatusDto { Pending = 0, Completed = 1, Corrected = 2, Cancelled = 3 }
public enum PaymentMethodDto { Cash = 0, Card = 1, Credit = 2, Mobile = 3, Mixed = 4 }