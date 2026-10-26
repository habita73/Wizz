namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ مستودع المنتجات باستخدام EF Core.</summary>
public sealed class ProductRepository : IProductRepository
{
    private readonly WizzDbContext _db;

    public ProductRepository(WizzDbContext db) => _db = db;

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Products.Include(p => p.Units).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
        => await _db.Products.Include(p => p.Units).FirstOrDefaultAsync(p => p.Barcode == barcode, ct);

    public async Task<IReadOnlyList<Product>> SearchAsync(string? query, int limit = 50, CancellationToken ct = default)
    {
        var q = _db.Products.Include(p => p.Units).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(p => p.Name.Contains(term) || (p.Barcode != null && p.Barcode.Contains(term)));
        }
        return await q.OrderBy(p => p.Name).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default)
        => await _db.Products.Include(p => p.Units).OrderBy(p => p.Name).ToListAsync(ct);

    public async Task AddAsync(Product product, CancellationToken ct = default)
    {
        await _db.Products.AddAsync(product, ct);
        foreach (var unit in product.Units)
            _db.Entry(unit).State = EntityState.Added;
    }

    public void Update(Product product)
    {
        _db.Products.Update(product);
        foreach (var unit in product.Units)
            _db.Entry(unit).State = unit.Id == Guid.Empty ? EntityState.Added : EntityState.Modified;
    }

    public void Remove(Product product) => _db.Products.Remove(product);

    public async Task<bool> ExistsBarcodeAsync(string barcode, Guid? excludeId = null, CancellationToken ct = default)
        => await _db.Products.AnyAsync(p => p.Barcode == barcode && (!excludeId.HasValue || p.Id != excludeId.Value), ct);

    public async Task<int> CountAsync(CancellationToken ct = default)
        => await _db.Products.CountAsync(ct);

    /// <summary>
    /// تُنفَّذ المقارنة في الذاكرة لأن SQLite لا يدعم مقارنات decimal داخل الاستعلام.
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetLowStockAsync(CancellationToken ct = default)
    {
        var products = await _db.Products.Include(p => p.Units)
            .Where(p => !p.IsService)
            .ToListAsync(ct);

        return products
            .Where(p => p.IsLowStock)
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .ToList();
    }
}