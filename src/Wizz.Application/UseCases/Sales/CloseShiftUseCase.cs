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
/// </summary>
public sealed class CloseShiftUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CloseShiftUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<CashierShiftDto>> ExecuteAsync(
        Guid shiftId,
        Guid closedByUserId,
        CancellationToken ct = default)
    {
        if (closedByUserId == Guid.Empty)
            return Result<CashierShiftDto>.Failure("معرّف المستخدم الذي يغلق الوردية مطلوب");

        var shift = await _unitOfWork.CashierShifts.GetByIdAsync(shiftId, ct);
        if (shift is null)
            return Result<CashierShiftDto>.Failure("الوردية غير موجودة");
        if (!shift.IsOpen)
            return Result<CashierShiftDto>.Failure("الوردية مغلقة بالفعل");

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            var sales = await _unitOfWork.Db.Set<Sale>()
                .AsNoTracking()
                .Where(s => s.ShiftId == shiftId)
                .Select(s => new { s.Status, s.Total, s.Paid, s.Remaining, s.IsCreditSale })
                .ToListAsync(ct);

            var active = sales.Where(s => s.Status != SaleStatus.Cancelled).ToList();

            shift.Close(
                closedByUserId,
                active.Sum(s => s.Total),
                active.Where(s => !s.IsCreditSale).Sum(s => s.Paid),
                active.Where(s => s.IsCreditSale).Sum(s => s.Remaining));

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
