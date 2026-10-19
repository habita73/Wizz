namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>عقد مستودع المبيعات.</summary>
public interface ISaleRepository
{
    Task<Sale?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetByCashierAsync(Guid cashierId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetPendingAsync(CancellationToken ct = default);
    Task AddAsync(Sale sale, CancellationToken ct = default);
    void Update(Sale sale);
    Task<decimal> GetTotalSalesAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<decimal> GetTotalPaidAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<decimal> GetTotalRemainingAsync(CancellationToken ct = default);
    Task<int> CountAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}