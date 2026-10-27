namespace Wizz.Application.UseCases.Store;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>إعداد المتجر لأول مرة (MVP: متجر واحد).</summary>
public sealed class InitializeStoreUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public InitializeStoreUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<StoreDto>> ExecuteAsync(StoreInputDto input, CancellationToken ct = default)
    {
        var exists = await _unitOfWork.Store.ExistsAsync(ct);
        if (exists)
            return Result<StoreDto>.Failure("المتجر معد بالفعل — استخدم التحديث");

        var store = new Store(input.Name, input.Address, input.Phone, input.TaxNumber, input.CommercialRegister, input.AllowCreditSales,
            inventoryEnabled: input.InventoryEnabled);
        await _unitOfWork.Store.AddAsync(store, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<StoreDto>.Success(MapToDto(store));
    }

    private static StoreDto MapToDto(Store s) => new(s.Id, s.Name, s.Address, s.Phone, s.TaxNumber, s.CommercialRegister, s.AllowCreditSales, s.Currency, s.TimeZone, s.InventoryEnabled);
}