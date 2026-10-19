namespace Wizz.Application.UseCases.Store;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;

/// <summary>الحصول على إعدادات المتجر.</summary>
public sealed class GetStoreSettingsUseCase
{
    private readonly IStoreRepository _store;

    public GetStoreSettingsUseCase(IStoreRepository store) => _store = store;

    public async Task<Result<StoreDto>> ExecuteAsync(CancellationToken ct = default)
    {
        var store = await _store.GetAsync(ct);
        if (store == null)
            return Result<StoreDto>.Failure("لم يتم إعداد المتجر بعد");

        return Result<StoreDto>.Success(new StoreDto(
            store.Id, store.Name, store.Address, store.Phone, store.TaxNumber, store.CommercialRegister,
            store.AllowCreditSales, store.Currency, store.TimeZone));
    }
}