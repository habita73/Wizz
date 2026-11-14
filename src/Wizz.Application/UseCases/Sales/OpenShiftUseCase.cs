namespace Wizz.Application.UseCases.Sales;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>فتح وردية كاشير يدويًا — تُرفض إن كانت هناك وردية مفتوحة لنفس (المتجر، الكاشير).</summary>
public sealed class OpenShiftUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public OpenShiftUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<CashierShiftDto>> ExecuteAsync(
        Guid cashierId,
        Guid openedByUserId,
        CancellationToken ct = default)
    {
        if (cashierId == Guid.Empty)
            return Result<CashierShiftDto>.Failure("معرّف الكاشير مطلوب");
        if (openedByUserId == Guid.Empty)
            return Result<CashierShiftDto>.Failure("معرّف المستخدم الذي يفتح الوردية مطلوب");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<CashierShiftDto>.Failure("لم يتم إعداد المتجر بعد");

        var existing = await _unitOfWork.CashierShifts.GetOpenAsync(store.Id, cashierId, ct);
        if (existing is not null)
            return Result<CashierShiftDto>.Failure("لدى الكاشير وردية مفتوحة بالفعل");

        var shift = new CashierShift(store.Id, cashierId, openedByUserId);

        try
        {
            await _unitOfWork.CashierShifts.AddAsync(shift, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // قيد فريد مفلتر يمنع ورديتين مفتوحتين — سباق فتح متزامن.
            return Result<CashierShiftDto>.Failure("تعذّر فتح الوردية — قد تكون فُتحت من جلسة أخرى");
        }

        return Result<CashierShiftDto>.Success(CashierShiftDto.FromDomain(shift));
    }
}
