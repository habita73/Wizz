namespace Wizz.Application.UseCases.Sales;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>نتيجة حساب النقد المتوقع لوردية.</summary>
internal sealed record CashComputation(
    decimal CashSalesTotal,
    int MixedPaymentsCount,
    decimal DepositsTotal,
    decimal WithdrawalsTotal,
    decimal ExpectedCash,
    IReadOnlyList<CashMovementDto> Movements);

/// <summary>
/// معادلة النقد المتوقع المعتمدة: الفكة + مبيعات Cash النشطة + الإيداعات − السحوبات.
/// Card وMobile وCredit خارج الصندوق، وMixed مستبعد مع إظهار عددها للتحذير.
/// </summary>
internal static class CashCalculator
{
    public static decimal Expected(decimal openingFloat, decimal cashSales, decimal deposits, decimal withdrawals)
        => decimal.Round(openingFloat + cashSales + deposits - withdrawals, 2, MidpointRounding.AwayFromZero);

    public static async Task<CashComputation> ComputeAsync(
        IUnitOfWork unitOfWork,
        Guid shiftId,
        decimal openingFloat,
        CancellationToken ct)
    {
        var sales = await unitOfWork.Db.Set<Sale>()
            .AsNoTracking()
            .Where(s => s.ShiftId == shiftId && s.Status != SaleStatus.Cancelled)
            .Select(s => new { s.PaymentMethod, s.Paid })
            .ToListAsync(ct);

        var cashSales = sales.Where(s => s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.Paid);
        var mixedCount = sales.Count(s => s.PaymentMethod == PaymentMethod.Mixed);

        var movements = await unitOfWork.CashMovements.GetForShiftAsync(shiftId, ct);
        var deposits = movements.Where(m => m.Type == CashMovementType.Deposit).Sum(m => m.Amount);
        var withdrawals = movements.Where(m => m.Type == CashMovementType.Withdrawal).Sum(m => m.Amount);

        return new CashComputation(
            Round(cashSales),
            mixedCount,
            Round(deposits),
            Round(withdrawals),
            Expected(openingFloat, cashSales, deposits, withdrawals),
            movements.Select(CashMovementDto.FromDomain).ToList());
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
