namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wizz.Application.Abstractions;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ وحدة العمل — ينسق العمليات عبر المستودعات.</summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly WizzDbContext _db;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(WizzDbContext db) => _db = db;

    public IProductRepository Products => new ProductRepository(_db);
    public ISaleRepository Sales => new SaleRepository(_db);
    public IStockMovementRepository StockMovements => new StockMovementRepository(_db);
    public IStoreRepository Store => new StoreRepository(_db);
    public IUserRepository Users => new UserRepository(_db);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
            throw new InvalidOperationException("معاملة قيد التنفيذ بالفعل");
        _transaction = await _db.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("لا توجد معاملة للالتزام بها");
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("لا توجد معاملة للتراجع عنها");
        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _db.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction != null)
            await _transaction.DisposeAsync();
        await _db.DisposeAsync();
    }
}