namespace Wizz.Application.DTOs;

using Wizz.Domain;

/// <summary>سطر مستند مخزون للعرض.</summary>
public sealed record StockDocumentLineDto(
    Guid Id,
    Guid ProductId,
    Guid UnitId,
    decimal Quantity,
    decimal BaseQuantityDelta,
    decimal QuantityBefore,
    decimal QuantityAfter,
    string? Reason
);

/// <summary>مستند مخزون للعرض.</summary>
public sealed record StockDocumentDto(
    Guid Id,
    Guid StoreId,
    StockDocumentType Type,
    string TypeText,
    string Number,
    DateOnly DocumentDate,
    DateTime CreatedAt,
    Guid? CreatedByUserId,
    string? Reason,
    string? Source,
    bool IsVoided,
    IReadOnlyList<StockDocumentLineDto> Lines
)
{
    public static StockDocumentDto FromDomain(StockDocument d) => new(
        d.Id,
        d.StoreId,
        d.Type,
        TypeTextOf(d.Type),
        d.Number,
        d.DocumentDate,
        d.CreatedAt,
        d.CreatedByUserId,
        d.Reason,
        d.Source,
        d.IsVoided,
        d.Lines.Select(l => new StockDocumentLineDto(
            l.Id, l.ProductId, l.UnitId, l.Quantity, l.BaseQuantityDelta,
            l.QuantityBefore, l.QuantityAfter, l.Reason)).ToList());

    public static string TypeTextOf(StockDocumentType type) => type switch
    {
        StockDocumentType.OpeningBalance => "رصيد افتتاحي",
        StockDocumentType.StockCount => "جرد",
        StockDocumentType.Waste => "إتلاف",
        StockDocumentType.SalesReturn => "مرتجع بيع",
        StockDocumentType.Purchase => "شراء",
        StockDocumentType.Adjustment => "تسوية",
        _ => "غير معروف"
    };
}

/// <summary>بند في بطاقة المخزون — مستند أو بيع نشط.</summary>
public sealed record StockCardEntryDto(
    DateOnly DocumentDate,
    DateTime CreatedAt,
    string Kind,
    string Number,
    decimal QuantityDelta,
    decimal RunningBalance,
    string? Reason,
    Guid? UserId
);

/// <summary>بطاقة مخزون منتج مع مطابقة الرصيد المخزَّن.</summary>
public sealed record StockCardDto(
    Guid ProductId,
    string ProductName,
    string BaseUnitSymbol,
    decimal StoredQuantity,
    decimal ComputedQuantity,
    IReadOnlyList<StockCardEntryDto> Entries
)
{
    public decimal Difference => ComputedQuantity - StoredQuantity;
    public bool IsReconciled => Difference == 0m;
}
