namespace Wizz.Application.DTOs;

/// <summary>سطر شراء جديد: الكمية وسعر الوحدة بالوحدة المختارة للمنتج.</summary>
public sealed record PurchaseLineInputDto(
    Guid ProductId,
    Guid UnitId,
    decimal Quantity,
    decimal UnitCost);

/// <summary>تقرير المشتريات. «إجمالي الشراء» ليس ربحًا ولا تكلفة مبيعات.</summary>
public sealed record PurchaseReportDto(
    DateOnly From,
    DateOnly To,
    int DocumentCount,
    int VoidedDocumentCount,
    decimal TotalPurchase,
    IReadOnlyList<PurchaseSupplierRowDto> BySupplier,
    IReadOnlyList<PurchaseProductRowDto> ByProduct);

public sealed record PurchaseSupplierRowDto(
    string SupplierName,
    int DocumentCount,
    decimal TotalPurchase);

public sealed record PurchaseProductRowDto(
    string ProductName,
    decimal Quantity,
    decimal TotalPurchase);
