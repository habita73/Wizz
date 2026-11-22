namespace Wizz.Application.Abstractions;

using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>عقد مستودع المبيعات.</summary>
public interface ISaleRepository
{
    Task<Sale?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>تحميل بيع بدون تتبع الكيانات (للقراءة أو للتعديل اليدوي لحالة الكيانات).</summary>
    Task<Sale?> GetByIdAsyncNoTracking(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetByCashierAsync(Guid cashierId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>كل المبيعات غير الملغاة التي ما زال عليها مبلغ متبقٍ (ديون)، مع خيار حصر المتأخرة فقط.</summary>
    Task<IReadOnlyList<Sale>> GetOutstandingAsync(bool overdueOnly = false, CancellationToken ct = default);

    /// <summary>تسديدات بيع واحد، مرتبة من الأحدث إلى الأقدم.</summary>
    Task<IReadOnlyList<DebtPayment>> GetPaymentsAsync(Guid saleId, CancellationToken ct = default);

    /// <summary>كل التسديدات ضمن نطاق زمني، للتقارير.</summary>
    Task<IReadOnlyList<DebtPayment>> GetPaymentsAsync(DateTime from, DateTime to, CancellationToken ct = default);

    Task AddPaymentAsync(DebtPayment payment, CancellationToken ct = default);

    /// <summary>
    /// أسماء الزبائن السابقة المطابقة لما كُتب، بعد توحيد الهجاء، مع عدد المبيعات
    /// والديون المفتوحة لكل اسم. تُستخدم لقائمة الاقتراحات عند البيع الآجل.
    /// </summary>
    Task<IReadOnlyList<CustomerSuggestionDto>> GetCustomerSuggestionsAsync(
        string? prefix, int limit = 8, CancellationToken ct = default);

    Task AddAsync(Sale sale, CancellationToken ct = default);
    void Update(Sale sale);
    Task<decimal> GetTotalSalesAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<decimal> GetTotalPaidAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<decimal> GetTotalRemainingAsync(CancellationToken ct = default);
    Task<int> CountAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}