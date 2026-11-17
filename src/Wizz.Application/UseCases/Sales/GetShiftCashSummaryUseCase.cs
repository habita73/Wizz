namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;

/// <summary>ملخص حي لنقد وردية مفتوحة — المتوقع حساب مباشر وليس لقطة مجمّدة.</summary>
public sealed class GetShiftCashSummaryUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public GetShiftCashSummaryUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<ShiftCashSummaryDto>> ExecuteAsync(Guid shiftId, CancellationToken ct = default)
    {
        var shift = await _unitOfWork.CashierShifts.GetByIdAsync(shiftId, ct);
        if (shift is null)
            return Result<ShiftCashSummaryDto>.Failure("الوردية غير موجودة");

        var cash = await CashCalculator.ComputeAsync(_unitOfWork, shiftId, shift.OpeningFloat, ct);

        return Result<ShiftCashSummaryDto>.Success(new ShiftCashSummaryDto(
            shiftId,
            shift.OpeningFloat,
            cash.CashSalesTotal,
            cash.DepositsTotal,
            cash.WithdrawalsTotal,
            cash.ExpectedCash,
            cash.MixedPaymentsCount,
            cash.Movements));
    }
}
