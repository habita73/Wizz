namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>مستودع حركات المخزون — قراءة سجل التدقيق وكتابة الحركات الجديدة.</summary>
public interface IStockMovementRepository
{
    Task AddAsync(StockMovement movement, CancellationToken ct = default);

    /// <summary>أحدث الحركات، مع إمكانية التصفية بمنتج.</summary>
    Task<IReadOnlyList<StockMovement>> GetRecentAsync(int limit = 100, Guid? productId = null, CancellationToken ct = default);

    Task<IReadOnlyList<StockMovement>> GetBySaleAsync(Guid saleId, CancellationToken ct = default);
}
