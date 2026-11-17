namespace Wizz.Application.UseCases.StockDocuments;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>
/// تقرير المشتريات: إجمالي الشراء ومستنداته للفترة، مع استبعاد الملغاة من الإجمالي
/// وعرض عددها منفصلًا. لا يُعرض أي رقم بعنوان «ربح» أو «تكلفة مبيعات».
/// </summary>
public sealed class GetPurchaseReportUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPurchaseReportUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<PurchaseReportDto>> ExecuteAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (to < from)
            return Result<PurchaseReportDto>.Failure("تاريخ نهاية الفترة يجب ألا يسبق تاريخ بدايتها");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<PurchaseReportDto>.Failure("لم يتم إعداد المتجر بعد");

        var all = await _unitOfWork.StockDocuments.GetByTypeAsync(
            StockDocumentType.Purchase, store.Id, from, to, includeVoided: true, ct);

        var active = all.Where(d => !d.IsVoided).ToList();
        var voidedCount = all.Count - active.Count;
        var total = Round(active.SelectMany(d => d.Lines).Sum(l => l.LineCost ?? 0m));

        var bySupplier = active
            .GroupBy(d => string.IsNullOrWhiteSpace(d.SupplierName) ? "بدون مورد" : d.SupplierName.Trim())
            .Select(g => new PurchaseSupplierRowDto(g.Key, g.Count(), Round(g.SelectMany(d => d.Lines).Sum(l => l.LineCost ?? 0m))))
            .OrderByDescending(r => r.TotalPurchase)
            .ToList();

        var productTotals = active
            .SelectMany(d => d.Lines)
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = Round(g.Sum(l => l.BaseQuantityDelta)), Cost = Round(g.Sum(l => l.LineCost ?? 0m)) })
            .ToList();

        var productNames = productTotals.Count == 0
            ? new Dictionary<Guid, string>()
            : await _unitOfWork.Db.Set<Product>()
                .AsNoTracking()
                .Where(p => productTotals.Select(t => t.ProductId).Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var byProduct = productTotals
            .Select(t => new PurchaseProductRowDto(
                productNames.TryGetValue(t.ProductId, out var name) ? name : "—",
                t.Quantity,
                t.Cost))
            .OrderByDescending(r => r.TotalPurchase)
            .ToList();

        return Result<PurchaseReportDto>.Success(new PurchaseReportDto(
            from,
            to,
            active.Count,
            voidedCount,
            total,
            bySupplier,
            byProduct));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
