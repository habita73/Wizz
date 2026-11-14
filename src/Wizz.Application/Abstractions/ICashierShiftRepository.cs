namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>مستودع ورديات الكاشير — الوردية المفتوحة تحكم نافذة تعديل المبيعات.</summary>
public interface ICashierShiftRepository
{
    Task<CashierShift?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>الوردية المفتوحة الحالية لزوج (المتجر، الكاشير)، أو null.</summary>
    Task<CashierShift?> GetOpenAsync(Guid storeId, Guid cashierId, CancellationToken ct = default);

    Task<IReadOnlyList<CashierShift>> GetRecentAsync(Guid storeId, int limit = 50, CancellationToken ct = default);

    Task AddAsync(CashierShift shift, CancellationToken ct = default);

    void Update(CashierShift shift);
}
