namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ مستودع ورديات الكاشير باستخدام EF Core.</summary>
public sealed class CashierShiftRepository : ICashierShiftRepository
{
    private readonly WizzDbContext _db;

    public CashierShiftRepository(WizzDbContext db) => _db = db;

    public async Task<CashierShift?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.CashierShifts.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<CashierShift?> GetOpenAsync(Guid storeId, Guid cashierId, CancellationToken ct = default)
        => await _db.CashierShifts
            .Where(s => s.StoreId == storeId && s.CashierId == cashierId && s.ClosedAt == null)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CashierShift>> GetRecentAsync(Guid storeId, int limit = 50, CancellationToken ct = default)
        => await _db.CashierShifts
            .AsNoTracking()
            .Where(s => s.StoreId == storeId)
            .OrderByDescending(s => s.OpenedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

    public async Task AddAsync(CashierShift shift, CancellationToken ct = default)
        => await _db.CashierShifts.AddAsync(shift, ct);

    public void Update(CashierShift shift)
    {
        var entry = _db.Entry(shift);
        if (entry.State == EntityState.Detached)
        {
            _db.CashierShifts.Attach(shift);
            entry.State = EntityState.Modified;
        }
    }
}
