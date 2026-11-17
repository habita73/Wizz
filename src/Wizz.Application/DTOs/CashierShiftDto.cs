namespace Wizz.Application.DTOs;

using Wizz.Domain;

/// <summary>وردية كاشير للعرض — الحالة مشتقة من وقت الإغلاق.</summary>
public sealed record CashierShiftDto(
    Guid Id,
    Guid StoreId,
    Guid CashierId,
    DateTime OpenedAt,
    Guid OpenedByUserId,
    DateTime? ClosedAt,
    Guid? ClosedByUserId,
    decimal SalesTotal,
    decimal CashTotal,
    decimal CreditTotal,
    bool IsOpen,
    decimal OpeningFloat,
    decimal? DepositsTotal,
    decimal? WithdrawalsTotal,
    decimal? ExpectedCash,
    decimal? CountedCash,
    decimal? CashDifference,
    string? CloseNote,
    bool HasCashCount
)
{
    public static CashierShiftDto FromDomain(CashierShift shift) => new(
        shift.Id,
        shift.StoreId,
        shift.CashierId,
        shift.OpenedAt,
        shift.OpenedByUserId,
        shift.ClosedAt,
        shift.ClosedByUserId,
        shift.SalesTotal,
        shift.CashTotal,
        shift.CreditTotal,
        shift.IsOpen,
        shift.OpeningFloat,
        shift.DepositsTotal,
        shift.WithdrawalsTotal,
        shift.ExpectedCash,
        shift.CountedCash,
        shift.CashDifference,
        shift.CloseNote,
        shift.HasCashCount);
}

/// <summary>سطر مرتجع بيع — الكمية بالوحدة المختارة ويرفض تجاوز المتبقي من البيع الأصلي.</summary>
public sealed record SalesReturnLineInputDto(
    Guid ProductId,
    Guid UnitId,
    decimal Quantity);
