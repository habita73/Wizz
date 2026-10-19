namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ مستودع المتجر.</summary>
public sealed class StoreRepository : IStoreRepository
{
    private readonly WizzDbContext _db;

    public StoreRepository(WizzDbContext db) => _db = db;

    public async Task<Store?> GetAsync(CancellationToken ct = default)
        => await _db.Stores.FirstOrDefaultAsync(ct);

    public async Task AddAsync(Store store, CancellationToken ct = default)
        => await _db.Stores.AddAsync(store, ct);

    public void Update(Store store) => _db.Stores.Update(store);

    public async Task<bool> ExistsAsync(CancellationToken ct = default)
        => await _db.Stores.AnyAsync(ct);
}