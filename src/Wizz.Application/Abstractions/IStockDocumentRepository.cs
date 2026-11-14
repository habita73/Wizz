namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>مستودع مستندات المخزون — المستندات هي مصدر الحقيقة.</summary>
public interface IStockDocumentRepository
{
    Task AddAsync(StockDocument document, CancellationToken ct = default);

    Task<StockDocument?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>هل للمنتج أي سطر مستند؟ يمنع الحذف النهائي وفق R-10.</summary>
    Task<bool> HasDocumentsForProductAsync(Guid productId, CancellationToken ct = default);

    /// <summary>أسطر مستندات منتج مع مستنداتها، اختياريًا مع الملغاة.</summary>
    Task<IReadOnlyList<StockDocumentLine>> GetLinesForProductAsync(
        Guid productId,
        Guid? storeId = null,
        bool includeVoided = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<StockDocument>> GetRecentAsync(Guid? storeId = null, int limit = 100, CancellationToken ct = default);
}
