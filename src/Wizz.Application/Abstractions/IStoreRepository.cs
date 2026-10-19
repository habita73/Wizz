namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>عقد مستودع إعدادات المتجر.</summary>
public interface IStoreRepository
{
    Task<Store?> GetAsync(CancellationToken ct = default);  // MVP: متجر واحد
    Task AddAsync(Store store, CancellationToken ct = default);
    void Update(Store store);
    Task<bool> ExistsAsync(CancellationToken ct = default);
}