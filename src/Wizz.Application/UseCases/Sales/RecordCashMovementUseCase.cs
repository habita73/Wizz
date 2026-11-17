namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;

/// <summary>
/// تسجيل إيداع أو سحب نقدي على وردية مفتوحة. الحركة لا تُعدَّل ولا تُحذف؛
/// التصحيح بحركة معاكسة بسبب إلزامي.
/// </summary>
public sealed class RecordCashMovementUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public RecordCashMovementUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<CashMovementDto>> ExecuteAsync(
        Guid shiftId,
        CashMovementType type,
        decimal amount,
        string reason,
        Guid createdByUserId,
        Guid? referencePurchaseId = null,
        CancellationToken ct = default)
    {
        if (type is not (CashMovementType.Deposit or CashMovementType.Withdrawal))
            return Result<CashMovementDto>.Failure("نوع الحركة النقدية غير معروف");
        if (amount <= 0)
            return Result<CashMovementDto>.Failure("مبلغ الحركة النقدية يجب أن يكون أكبر من صفر");
        if (decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            return Result<CashMovementDto>.Failure("مبلغ الحركة النقدية يقبل منزلتين عشريتين على الأكثر");
        if (string.IsNullOrWhiteSpace(reason))
            return Result<CashMovementDto>.Failure("سبب الحركة النقدية مطلوب");
        if (createdByUserId == Guid.Empty)
            return Result<CashMovementDto>.Failure("معرّف الفاعل مطلوب");

        var shift = await _unitOfWork.CashierShifts.GetByIdAsync(shiftId, ct);
        if (shift is null)
            return Result<CashMovementDto>.Failure("الوردية غير موجودة");
        if (!shift.IsOpen)
            return Result<CashMovementDto>.Failure("لا يمكن تسجيل حركة نقدية على وردية مغلقة");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<CashMovementDto>.Failure("لم يتم إعداد المتجر بعد");

        var movement = new CashMovement(
            store.Id, shiftId, type, amount, reason, createdByUserId, referencePurchaseId);

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            await _unitOfWork.CashMovements.AddAsync(movement, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await StockWorkflow.CommitIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
        }
        catch
        {
            await StockWorkflow.RollbackIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
            throw;
        }

        return Result<CashMovementDto>.Success(CashMovementDto.FromDomain(movement));
    }
}
