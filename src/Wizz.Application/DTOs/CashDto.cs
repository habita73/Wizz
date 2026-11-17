namespace Wizz.Application.DTOs;

using Wizz.Domain;

/// <summary>حركة نقدية للعرض.</summary>
public sealed record CashMovementDto(
    Guid Id,
    Guid StoreId,
    Guid ShiftId,
    string Type,
    decimal Amount,
    string Reason,
    DateTime OccurredAt,
    Guid CreatedByUserId,
    Guid? ReferencePurchaseId)
{
    public static CashMovementDto FromDomain(CashMovement movement) => new(
        movement.Id,
        movement.StoreId,
        movement.ShiftId,
        movement.Type == CashMovementType.Deposit ? "إيداع" : "سحب",
        movement.Amount,
        movement.Reason,
        movement.OccurredAt,
        movement.CreatedByUserId,
        movement.ReferencePurchaseId);
}

/// <summary>ملخص حي لوردية مفتوحة. المتوقع حساب مباشر وليس لقطة مجمّدة.</summary>
public sealed record ShiftCashSummaryDto(
    Guid ShiftId,
    decimal OpeningFloat,
    decimal CashSalesTotal,
    decimal DepositsTotal,
    decimal WithdrawalsTotal,
    decimal ExpectedCash,
    int MixedPaymentsCount,
    IReadOnlyList<CashMovementDto> Movements);

/// <summary>صف في تقرير الصندوق. الوردية المفتوحة «مباشر» والمغلقة لقطة مجمّدة.</summary>
public sealed record CashReportRowDto(
    Guid ShiftId,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    Guid CashierId,
    string CashierName,
    bool IsOpen,
    bool HasCashCount,
    bool IsLive,
    decimal OpeningFloat,
    decimal CashSalesTotal,
    decimal DepositsTotal,
    decimal WithdrawalsTotal,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? CashDifference,
    string? CloseNote,
    string StatusLabel);

/// <summary>تقرير الصندوق للفترة المطلوبة.</summary>
public sealed record CashReportDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CashReportRowDto> Rows,
    int OpenShiftCount,
    int ClosedShiftCount,
    int LegacyShiftCount);
