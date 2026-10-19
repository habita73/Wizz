namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>عقد مستودع المنتجات — يفصل طبقة التطبيق عن تفاصيل التخزين.</summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> SearchAsync(string? query, int limit = 50, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
    void Update(Product product);
    void Remove(Product product);
    Task<bool> ExistsBarcodeAsync(string barcode, Guid? excludeId = null, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}