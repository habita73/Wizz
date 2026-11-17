namespace Wizz.Application.UseCases.Sales;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;

/// <summary>
/// إغلاق وردية الكاشير وتجميد إجمالياتها. التعريف المعتمد:
/// SalesTotal = مجموع Total لغير الملغى، CashTotal = مجموع Paid لغير الآجلة،
/// CreditTotal = مجموع Remaining للآجلة.
/// عند تمرير countedCash يُجرى عدّ نقدي ويُجمَّد الفرق؛ وبدونه يعود إلى سلوك المرحلة 2.
/// </summary>
public sealed class CloseShiftUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CloseShiftUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<CashierShiftDto>> ExecuteAsync(
        Guid shiftId,
        Guid closedByUserId,
        decimal? countedCash = null,
        string? closeNote = null,
        CancellationToken ct = default)
    {
        if (closedByUserId == Guid.Empty)
            return Result<CashierShiftDto>.Failure("معرّف المستخدم الذي يغلق الوردية مطلوب");
        if (countedCash.HasValue && countedCash.Value < 0)
            return Result<CashierShiftDto>.Failure("النقد المعدود لا يمكن أن يكون سالبًا");
        if (countedCash.HasValue &&
            decimal.Round(countedCash.Value, 2, MidpointRounding.ToZero) != countedCash.Value)
            return Result<CashierShiftDto>.Failure("النقد المعدود يقبل منزلتين عشريتين على الأكثر");

        var shift = await _unitOfWork.CashierShifts.GetByIdAsync(shiftId, ct);
        if (shift is null)
            return Result<CashierShiftDto>.Failure("الوردية غير موجودة");
        if (!shift.IsOpen)
            return Result<CashierShiftDto>.Failure("الوردية مغلقة بالفعل");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<CashierShiftDto>.Failure("لم يتم إعداد المتجر بعد");

        var sales = await _unitOfWork.Db.Set<Sale>()
            .AsNoTracking()
            .Where(s => s.ShiftId == shiftId)
            .Select(s => new { s.Status, s.Total, s.Paid, s.Remaining, s.IsCreditSale })
            .ToListAsync(ct);

        var active = sales.Where(s => s.Status != SaleStatus.Cancelled).ToList();
        var salesTotal = active.Sum(s => s.Total);
        var cashTotal = active.Where(s => !s.IsCreditSale).Sum(s => s.Paid);
        var creditTotal = active.Where(s => s.IsCreditSale).Sum(s => s.Remaining);

        CashComputation? cash = null;
        decimal? difference = null;

        // العدّ يُجرى فقط حين تفعّل وحدة الصندوق ويُمرَّر المبلغ المعدود صراحةً.
        if (store.CashDrawerEnabled && countedCash.HasValue)
        {
            cash = await CashCalculator.ComputeAsync(_unitOfWork, shiftId, shift.OpeningFloat, ct);
            difference = decimal.Round(countedCash.Value - cash.ExpectedCash, 2, MidpointRounding.AwayFromZero);

            if (Math.Abs(difference.Value) > store.CashDifferenceTolerance &&
                string.IsNullOrWhiteSpace(closeNote))
            {
                return Result<CashierShiftDto>.Failure(
                    "فرق الإغلاق يتجاوز حدّ التسامح — يلزم إدخال ملاحظة توضّح السبب");
            }
        }

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            if (cash is not null && countedCash.HasValue)
            {
                shift.CloseWithCash(
                    closedByUserId,
                    salesTotal,
                    cashTotal,
                    creditTotal,
                    cash.DepositsTotal,
                    cash.WithdrawalsTotal,
                    cash.ExpectedCash,
                    countedCash.Value,
                    closeNote);
            }
            else
            {
                shift.Close(closedByUserId, salesTotal, cashTotal, creditTotal);
            }

            _unitOfWork.CashierShifts.Update(shift);
            await _unitOfWork.SaveChangesAsync(ct);
            await StockWorkflow.CommitIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
        }
        catch
        {
            await StockWorkflow.RollbackIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
            throw;
        }

        return Result<CashierShiftDto>.Success(CashierShiftDto.FromDomain(shift));
    }
}
