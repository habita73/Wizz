namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;

/// <summary>الوردية المفتوحة الحالية للكاشير، أو null إن لم توجد.</summary>
public sealed class GetOpenShiftUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public GetOpenShiftUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<CashierShiftDto?>> ExecuteAsync(Guid cashierId, CancellationToken ct = default)
    {
        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<CashierShiftDto?>.Failure("لم يتم إعداد المتجر بعد");

        var shift = await _unitOfWork.CashierShifts.GetOpenAsync(store.Id, cashierId, ct);
        return Result<CashierShiftDto?>.Success(shift is null ? null : CashierShiftDto.FromDomain(shift));
    }
}
