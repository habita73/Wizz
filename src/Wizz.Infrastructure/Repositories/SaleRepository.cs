namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ مستودع المبيعات باستخدام EF Core.</summary>
public sealed class SaleRepository : ISaleRepository
{
    private readonly WizzDbContext _db;

    public SaleRepository(WizzDbContext db) => _db = db;

    public async Task<Sale?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Sales
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .Include(s => s.Items)
                .ThenInclude(i => i.Unit)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Sale?> GetByIdAsyncNoTracking(Guid id, CancellationToken ct = default)
        => await _db.Sales
            .AsNoTracking()
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .Include(s => s.Items)
                .ThenInclude(i => i.Unit)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Sale>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => await _db.Sales
            .Include(s => s.Items)
            .Where(s => s.SaleDate >= from && s.SaleDate <= to)
            .OrderByDescending(s => s.SaleDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sale>> GetByCashierAsync(Guid cashierId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = _db.Sales.Include(s => s.Items).Where(s => s.CashierId == cashierId);
        if (from.HasValue) q = q.Where(s => s.SaleDate >= from.Value);
        if (to.HasValue) q = q.Where(s => s.SaleDate <= to.Value);
        return await q.OrderByDescending(s => s.SaleDate).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Sale>> GetPendingAsync(CancellationToken ct = default)
        => await _db.Sales
            .Include(s => s.Items)
            .Where(s => s.Status == SaleStatus.Pending)
            .OrderBy(s => s.SaleDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sale>> GetOutstandingAsync(bool overdueOnly = false, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var query = _db.Sales
            .Include(s => s.Items)
            .Where(s => s.Status != SaleStatus.Cancelled && s.Remaining > 0);

        if (overdueOnly)
        {
            query = query.Where(s => s.DueDate != null && s.DueDate < now);
        }

        return await query
            .OrderBy(s => s.DueDate == null)
            .ThenBy(s => s.DueDate)
            .ThenBy(s => s.SaleDate)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Sale sale, CancellationToken ct = default)
    {
        await _db.Sales.AddAsync(sale, ct);
        foreach (var item in sale.Items)
            _db.Entry(item).State = EntityState.Added;
    }

    public void Update(Sale sale)
    {
        _db.Entry(sale).State = EntityState.Modified;
        foreach (var item in sale.Items)
        {
            var entry = _db.Entry(item);
            // العنصر الجديد (غيرfurt من قاعدة البيانات) يُضاف، والعنصر الموجود يُحدّث
            entry.State = entry.State == EntityState.Detached ? EntityState.Added : EntityState.Modified;
        }

        // حذف الأصناف القديمة التي أزيلت من المجموعة (مثلاً عند تعديل البيع وتقليل الكمية أو حذف صنف)
        var trackedSaleItems = _db.ChangeTracker.Entries<SaleItem>()
            .Where(e => e.Entity.SaleId == sale.Id && e.State == EntityState.Unchanged)
            .ToList();
        foreach (var entry in trackedSaleItems)
        {
            if (!sale.Items.Any(i => i.Id == entry.Entity.Id))
                entry.State = EntityState.Deleted;
        }
    }

    public async Task<decimal> GetTotalSalesAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => await _db.Sales.Where(s => s.SaleDate >= from && s.SaleDate <= to && s.Status != SaleStatus.Cancelled)
            .SumAsync(s => s.Total, ct);

    public async Task<decimal> GetTotalPaidAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => await _db.Sales.Where(s => s.SaleDate >= from && s.SaleDate <= to && s.Status != SaleStatus.Cancelled)
            .SumAsync(s => s.Paid, ct);

    public async Task<decimal> GetTotalRemainingAsync(CancellationToken ct = default)
        => await _db.Sales.Where(s => s.Status != SaleStatus.Cancelled && s.Remaining > 0)
            .SumAsync(s => s.Remaining, ct);

    public async Task<int> CountAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = _db.Sales.AsQueryable();
        if (from.HasValue) q = q.Where(s => s.SaleDate >= from.Value);
        if (to.HasValue) q = q.Where(s => s.SaleDate <= to.Value);
        return await q.CountAsync(ct);
    }
}