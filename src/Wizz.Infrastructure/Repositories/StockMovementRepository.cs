namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ مستودع حركات المخزون باستخدام EF Core.</summary>
public sealed class StockMovementRepository : IStockMovementRepository
{
    private readonly WizzDbContext _db;

    public StockMovementRepository(WizzDbContext db) => _db = db;

    public async Task AddAsync(StockMovement movement, CancellationToken ct = default)
        => await _db.StockMovements.AddAsync(movement, ct);

    public async Task<IReadOnlyList<StockMovement>> GetRecentAsync(int limit = 100, Guid? productId = null, CancellationToken ct = default)
    {
        var query = _db.StockMovements
            .Include(m => m.Product)
                .ThenInclude(p => p!.Units)
            .AsQueryable();

        if (productId.HasValue)
            query = query.Where(m => m.ProductId == productId.Value);

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockMovement>> GetBySaleAsync(Guid saleId, CancellationToken ct = default)
        => await _db.StockMovements
            .Include(m => m.Product)
                .ThenInclude(p => p!.Units)
            .Where(m => m.SaleId == saleId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
}
