namespace Wizz.Application.Abstractions;

using Microsoft.EntityFrameworkCore;

/// <summary>وحدة العمل — تنسق العمليات عبر مستودعات متعددة في معاملة واحدة.</summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    IProductRepository Products { get; }
    ISaleRepository Sales { get; }
    IStockMovementRepository StockMovements { get; }
    IStoreRepository Store { get; }
    IUserRepository Users { get; }

    DbContext Db { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}