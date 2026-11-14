namespace Wizz.Application.UseCases.StockDocuments;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>
/// بطاقة المخزون — تُبنى قراءة موحّدة من المستندات والمبيعات النشطة، بلا جدول مكرر.
/// الترتيب: (DocumentDate, CreatedAt)، والرصيد الجاري عمود محسوب في العرض.
/// </summary>
public sealed class GetStockCardUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public GetStockCardUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<StockCardDto>> ExecuteAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _unitOfWork.Products.GetByIdAsync(productId, ct);
        if (product == null)
            return Result<StockCardDto>.Failure("منتج غير موجود");

        if (product.IsService)
            return Result<StockCardDto>.Failure("الخدمات لا تملك بطاقة مخزون");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<StockCardDto>.Failure("لم يتم إعداد المتجر بعد");

        var entries = new List<(DateOnly Date, DateTime CreatedAt, string Kind, string Number, decimal Delta, string? Reason, Guid? UserId)>();

        var documentLines = await _unitOfWork.StockDocuments.GetLinesForProductAsync(productId, store.Id, ct: ct);
        foreach (var line in documentLines)
        {
            var document = line.StockDocument;
            if (document == null) continue;

            entries.Add((
                document.DocumentDate,
                document.CreatedAt,
                StockDocumentDto.TypeTextOf(document.Type),
                document.Number,
                line.BaseQuantityDelta,
                line.Reason ?? document.Reason,
                document.CreatedByUserId));
        }

        // كل المبيعات التي خصمت المخزون فعلًا، بما فيها الملغاة.
        // البيع الملغى يظهر كخروج أصلي ثم دخول إلغاء بتاريخ CancelledAt، فمجموعه الصافي صفر،
        // ويبقى الرصيد المخزَّن مطابقًا لمعادلة المستندات. المبيعات بلا خصم مخزون لا تُدرج.
        var appliedSales = await _unitOfWork.Db.Set<Sale>()
            .AsNoTracking()
            .Include(s => s.Items)
                .ThenInclude(i => i.Unit)
            .Where(s => s.StoreId == store.Id && s.InventoryApplied)
            .ToListAsync(ct);

        foreach (var sale in appliedSales)
        {
            var productQuantity = sale.Items
                .Where(i => i.ProductId == productId && i.Unit != null)
                .Sum(i => StockQuantity.ToBase(i.Unit!, i.Quantity));

            if (productQuantity == 0) continue;

            var saleNumber = $"SALE-{sale.Id.ToString("N")[..8]}";

            entries.Add((
                StockWorkflow.BusinessDate(store, sale.SaleDate),
                sale.SaleDate,
                "بيع",
                saleNumber,
                -StockQuantity.Normalize(productQuantity),
                null,
                sale.CashierId));

            if (sale.Status == SaleStatus.Cancelled)
            {
                var cancelledAt = sale.CancelledAt ?? sale.SaleDate;
                entries.Add((
                    StockWorkflow.BusinessDate(store, cancelledAt),
                    cancelledAt,
                    "إلغاء بيع",
                    $"VOID-{sale.Id.ToString("N")[..8]}",
                    StockQuantity.Normalize(productQuantity),
                    sale.CancelReason,
                    sale.CancelledByUserId));
            }
        }

        var ordered = entries
            .OrderBy(e => e.Date)
            .ThenBy(e => e.CreatedAt)
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ThenBy(e => e.Number, StringComparer.Ordinal)
            .ToList();

        var running = 0m;
        var rows = new List<StockCardEntryDto>(ordered.Count);
        foreach (var entry in ordered)
        {
            running = StockQuantity.Normalize(running + entry.Delta);
            rows.Add(new StockCardEntryDto(
                entry.Date, entry.CreatedAt, entry.Kind, entry.Number, entry.Delta,
                running, entry.Reason, entry.UserId));
        }

        var computed = StockQuantity.Normalize(rows.Sum(r => r.QuantityDelta));
        var stored = StockQuantity.Normalize(product.StockQuantity);

        return Result<StockCardDto>.Success(new StockCardDto(
            product.Id,
            product.Name,
            product.GetBaseUnit()?.Symbol ?? string.Empty,
            stored,
            computed,
            rows));
    }
}
