namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>
/// مستودع الحركات النقدية. التسجيل فقط: لا تعديل ولا حذف لحركة نقدية بعد إنشائها،
/// والتصحيح بحركة معاكسة بسبب إلزامي.
/// </summary>
public interface ICashMovementRepository
{
    Task AddAsync(CashMovement movement, CancellationToken ct = default);

    Task<CashMovement?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<CashMovement>> GetForShiftAsync(Guid shiftId, CancellationToken ct = default);

    Task<IReadOnlyList<CashMovement>> GetForShiftsAsync(
        IReadOnlyCollection<Guid> shiftIds,
        CancellationToken ct = default);
}
