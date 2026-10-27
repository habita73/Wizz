namespace Wizz.Application.DTOs;

using Wizz.Domain;

/// <summary>حركة مخزون للعرض في سجل الحركات.</summary>
public sealed record StockMovementDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string BaseUnitSymbol,
    Guid? SaleId,
    StockMovementType Type,
    decimal QuantityDelta,
    decimal QuantityBefore,
    decimal QuantityAfter,
    string? Reason,
    Guid? CreatedByUserId,
    DateTime CreatedAt
)
{
    /// <summary>نص عربي لنوع الحركة.</summary>
    public string TypeText => Type switch
    {
        StockMovementType.OpeningBalance => "رصيد افتتاحي",
        StockMovementType.Sale => "بيع",
        StockMovementType.SaleCancellation => "إلغاء بيع",
        StockMovementType.SaleCorrection => "تصحيح بيع",
        StockMovementType.ManualAdjustment => "تعديل يدوي",
        _ => "غير معروف"
    };

    /// <summary>تحويل كيان الحركة إلى DTO.</summary>
    public static StockMovementDto FromDomain(StockMovement m) => new(
        m.Id,
        m.ProductId,
        m.Product?.Name ?? "",
        m.Product?.GetBaseUnit()?.Symbol ?? "",
        m.SaleId,
        m.Type,
        m.QuantityDelta,
        m.QuantityBefore,
        m.QuantityAfter,
        m.Reason,
        m.CreatedByUserId,
        m.CreatedAt);
}
