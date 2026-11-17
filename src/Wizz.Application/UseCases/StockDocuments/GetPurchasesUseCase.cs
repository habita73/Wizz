namespace Wizz.Application.UseCases.StockDocuments;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>قائمة مستندات المشتريات SHR للفترة، مع أو بدون الملغاة.</summary>
public sealed class GetPurchasesUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPurchasesUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<IReadOnlyList<StockDocumentDto>>> ExecuteAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        bool includeVoided = true,
        CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && to.Value < from.Value)
            return Result<IReadOnlyList<StockDocumentDto>>.Failure("تاريخ نهاية الفترة يجب ألا يسبق تاريخ بدايتها");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<IReadOnlyList<StockDocumentDto>>.Failure("لم يتم إعداد المتجر بعد");

        var documents = await _unitOfWork.StockDocuments.GetByTypeAsync(
            StockDocumentType.Purchase, store.Id, from, to, includeVoided, ct);

        return Result<IReadOnlyList<StockDocumentDto>>.Success(
            documents.Select(StockDocumentDto.FromDomain).ToList());
    }
}
