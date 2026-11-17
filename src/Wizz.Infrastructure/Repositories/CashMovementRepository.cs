namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ مستودع الحركات النقدية باستخدام EF Core.</summary>
public sealed class CashMovementRepository : ICashMovementRepository
{
    private readonly WizzDbContext _db;

    public CashMovementRepository(WizzDbContext db) => _db = db;

    public async Task AddAsync(CashMovement movement, CancellationToken ct = default)
        => await _db.CashMovements.AddAsync(movement, ct);

    public async Task<CashMovement?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.CashMovements.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<CashMovement>> GetForShiftAsync(Guid shiftId, CancellationToken ct = default)
        => await _db.CashMovements
            .AsNoTracking()
            .Where(m => m.ShiftId == shiftId)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CashMovement>> GetForShiftsAsync(
        IReadOnlyCollection<Guid> shiftIds,
        CancellationToken ct = default)
    {
        if (shiftIds.Count == 0)
            return Array.Empty<CashMovement>();

        return await _db.CashMovements
            .AsNoTracking()
            .Where(m => shiftIds.Contains(m.ShiftId))
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);
    }
}
