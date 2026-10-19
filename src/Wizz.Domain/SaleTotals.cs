namespace Wizz.Domain;

/// <summary>النتائج المالية المحسوبة لعملية بيع غير محفوظة بعد.</summary>
public sealed record SaleTotals(
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal Paid,
    decimal Remaining);
