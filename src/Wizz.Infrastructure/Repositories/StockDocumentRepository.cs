namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ مستودع مستندات المخزون باستخدام EF Core.</summary>
public sealed class StockDocumentRepository : IStockDocumentRepository
{
    private readonly WizzDbContext _db;

    public StockDocumentRepository(WizzDbContext db) => _db = db;

    public async Task AddAsync(StockDocument document, CancellationToken ct = default)
    {
        await _db.StockDocuments.AddAsync(document, ct);
        foreach (var line in document.Lines)
            _db.Entry(line).State = EntityState.Added;
    }

    public async Task<StockDocument?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.StockDocuments
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<bool> HasDocumentsForProductAsync(Guid productId, CancellationToken ct = default)
        => await _db.StockDocumentLines.AnyAsync(l => l.ProductId == productId, ct);

    public async Task<IReadOnlyList<StockDocumentLine>> GetLinesForProductAsync(
        Guid productId,
        Guid? storeId = null,
        bool includeVoided = false,
        CancellationToken ct = default)
    {
        var query = _db.StockDocumentLines
            .AsNoTracking()
            .Include(l => l.StockDocument)
            .Where(l => l.ProductId == productId);

        if (storeId.HasValue)
            query = query.Where(l => l.StockDocument!.StoreId == storeId.Value);

        if (!includeVoided)
            query = query.Where(l => !l.StockDocument!.IsVoided);

        return await query
            .OrderBy(l => l.StockDocument!.DocumentDate)
            .ThenBy(l => l.StockDocument!.CreatedAt)
            .ThenBy(l => l.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockDocument>> GetRecentAsync(Guid? storeId = null, int limit = 100, CancellationToken ct = default)
    {
        var query = _db.StockDocuments
            .AsNoTracking()
            .Include(d => d.Lines)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(d => d.StoreId == storeId.Value);

        return await query
            .OrderByDescending(d => d.DocumentDate)
            .ThenByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }
}
