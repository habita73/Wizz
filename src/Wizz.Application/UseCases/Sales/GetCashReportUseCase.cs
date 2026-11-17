namespace Wizz.Application.UseCases.Sales;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;

/// <summary>
/// تقرير الورديات للفترة. الوردية المفتوحة موسومة «مباشر» لأن أرقامها حساب حي،
/// والمغلقة تعرض اللقطة المجمّدة حرفيًا. الورديات السابقة بلا عدّ تُوسم بوضوح.
/// </summary>
public sealed class GetCashReportUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public GetCashReportUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<CashReportDto>> ExecuteAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (to < from)
            return Result<CashReportDto>.Failure("تاريخ نهاية الفترة يجب ألا يسبق تاريخ بدايتها");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<CashReportDto>.Failure("لم يتم إعداد المتجر بعد");

        var shifts = await _unitOfWork.CashierShifts.GetRecentAsync(store.Id, 500, ct);
        var inRange = shifts
            .Where(s => StockWorkflow.BusinessDate(store, s.OpenedAt) >= from
                        && StockWorkflow.BusinessDate(store, s.OpenedAt) <= to)
            .OrderBy(s => s.OpenedAt)
            .ToList();

        var cashierIds = inRange.Select(s => s.CashierId).Distinct().ToList();
        var names = cashierIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _unitOfWork.Db.Set<User>()
                .AsNoTracking()
                .Where(u => cashierIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var rows = new List<CashReportRowDto>(inRange.Count);
        foreach (var shift in inRange)
        {
            decimal cashSales;
            decimal deposits;
            decimal withdrawals;
            decimal expected;

            if (shift.IsOpen)
            {
                var live = await CashCalculator.ComputeAsync(_unitOfWork, shift.Id, shift.OpeningFloat, ct);
                cashSales = live.CashSalesTotal;
                deposits = live.DepositsTotal;
                withdrawals = live.WithdrawalsTotal;
                expected = live.ExpectedCash;
            }
            else
            {
                // اللقطة المجمّدة هي مصدر الحقيقة بعد الإغلاق — لا تُعاد حسابها.
                deposits = shift.DepositsTotal ?? 0m;
                withdrawals = shift.WithdrawalsTotal ?? 0m;
                expected = shift.ExpectedCash ?? 0m;
                cashSales = shift.HasCashCount
                    ? Round(expected - shift.OpeningFloat - deposits + withdrawals)
                    : 0m;
            }

            var label = shift.IsOpen
                ? "مباشر"
                : shift.HasCashCount
                    ? "مغلق"
                    : "بيانات سابقة — بلا عدّ نقدي";

            rows.Add(new CashReportRowDto(
                shift.Id,
                shift.OpenedAt,
                shift.ClosedAt,
                shift.CashierId,
                names.TryGetValue(shift.CashierId, out var name) ? name : "—",
                shift.IsOpen,
                shift.HasCashCount,
                shift.IsOpen,
                shift.OpeningFloat,
                cashSales,
                deposits,
                withdrawals,
                expected,
                shift.CountedCash,
                shift.CashDifference,
                shift.CloseNote,
                label));
        }

        return Result<CashReportDto>.Success(new CashReportDto(
            from,
            to,
            rows,
            rows.Count(r => r.IsOpen),
            rows.Count(r => !r.IsOpen),
            rows.Count(r => !r.HasCashCount)));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
