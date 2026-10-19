namespace Wizz.Application.DTOs;

/// <summary>بيانات الإيصال للطباعة.</summary>
public sealed record ReceiptDto(
    Guid SaleId,
    string SaleNumber,
    DateTime SaleDate,
    StoreDto Store,
    IReadOnlyList<ReceiptLineDto> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal Paid,
    decimal Remaining,
    PaymentMethodDto PaymentMethod,
    string? CashierName,
    bool IsCreditSale,
    DateTime? DueDate,
    string? Notes
);

public sealed record ReceiptLineDto(
    string ProductName,
    decimal Quantity,
    string UnitSymbol,
    decimal UnitPrice,
    decimal LineTotal
);

/// <summary>تقرير مبيعات لفترة.</summary>
public sealed record SalesReportDto(
    DateTime From,
    DateTime To,
    int TotalSalesCount,
    decimal TotalSalesAmount,
    decimal TotalPaid,
    decimal TotalRemaining,
    decimal TotalDiscount,
    IReadOnlyList<PaymentMethodBreakdownDto> PaymentBreakdown,
    IReadOnlyList<DailySalesDto> DailySales
);

public sealed record PaymentMethodBreakdownDto(
    PaymentMethodDto Method,
    int Count,
    decimal Amount
);

public sealed record DailySalesDto(
    DateTime Date,
    int Count,
    decimal Total,
    decimal Paid,
    decimal Remaining
);