namespace Wizz.Application.UseCases.StockDocuments;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>
/// مستند شراء SHR: يزيد المخزون بالوحدة الأساسية داخل معاملة، ويخصّص رقمًا من تسلسل SHR
/// المستقل، ويخزّن تكلفة السطر دون اشتقاق ربح أو تكلفة مبيعات. لا يُنشأ جدول موردين ولا ذمم.
/// </summary>
public sealed class CreatePurchaseUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CreatePurchaseUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<StockDocumentDto>> ExecuteAsync(
        DateOnly documentDate,
        string? supplierName,
        string? supplierInvoiceNumber,
        IReadOnlyList<PurchaseLineInputDto> lines,
        Guid? createdByUserId,
        CancellationToken ct = default)
    {
        if (lines == null || lines.Count == 0)
            return Result<StockDocumentDto>.Failure("يجب أن يتضمن الشراء سطرًا واحدًا على الأقل");
        if (lines.Any(l => l.Quantity <= 0))
            return Result<StockDocumentDto>.Failure("كمية الشراء يجب أن تكون أكبر من صفر");
        if (lines.Any(l => l.UnitCost < 0))
            return Result<StockDocumentDto>.Failure("سعر الشراء لا يمكن أن يكون سالبًا");
        if (lines.Any(l => decimal.Round(l.UnitCost, 2, MidpointRounding.ToZero) != l.UnitCost))
            return Result<StockDocumentDto>.Failure("سعر الشراء يقبل منزلتين عشريتين على الأكثر");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<StockDocumentDto>.Failure("لم يتم إعداد المتجر بعد");
        if (!store.InventoryEnabled)
            return Result<StockDocumentDto>.Failure("تتبع المخزون معطّل — لا يُسجَّل شراء مخزوني");
        if (documentDate > StockWorkflow.Today(store))
            return Result<StockDocumentDto>.Failure("تاريخ المستند لا يمكن أن يكون في المستقبل");

        var prepared = new List<(Product Product, UnitOfMeasure Unit, PurchaseLineInputDto Line, decimal BaseQuantity)>();

        foreach (var line in lines)
        {
            var product = await _unitOfWork.Products.GetByIdAsync(line.ProductId, ct);
            if (product == null)
                return Result<StockDocumentDto>.Failure($"منتج غير موجود: {line.ProductId}");
            if (product.IsService)
                return Result<StockDocumentDto>.Failure("الخدمات لا تملك مخزونًا — لا تُشترى كمخزون");

            var unit = product.GetUnitById(line.UnitId);
            if (unit == null)
                return Result<StockDocumentDto>.Failure($"وحدة غير موجودة للمنتج: {product.Name}");

            var baseQuantity = StockQuantity.ToBase(unit, line.Quantity);
            if (baseQuantity <= 0)
                return Result<StockDocumentDto>.Failure("كمية الشراء بالوحدة الأساسية يجب أن تكون أكبر من صفر");

            prepared.Add((product, unit, line, baseQuantity));
        }

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            var number = await DocumentNumbering.AllocateAsync(
                _unitOfWork.Db, store.Id, StockDocumentType.Purchase, documentDate,
                store.DocumentYearDigits, ct);

            var documentLines = new List<StockDocumentLine>();
            foreach (var entry in prepared)
            {
                var before = StockQuantity.Normalize(entry.Product.StockQuantity);
                var after = StockQuantity.Normalize(before + entry.BaseQuantity);

                documentLines.Add(new StockDocumentLine(
                    entry.Product.Id,
                    entry.Unit.Id,
                    entry.Line.Quantity,
                    entry.BaseQuantity,
                    before,
                    after,
                    unitCost: entry.Line.UnitCost));

                entry.Product.ApplyStockChange(entry.BaseQuantity);
            }

            var document = new StockDocument(
                store.Id,
                StockDocumentType.Purchase,
                number,
                documentDate,
                createdByUserId,
                reason: null,
                lines: documentLines,
                supplierName: supplierName,
                supplierInvoiceNumber: supplierInvoiceNumber);

            await _unitOfWork.StockDocuments.AddAsync(document, ct);

            foreach (var product in prepared.Select(p => p.Product).DistinctBy(p => p.Id))
                _unitOfWork.Products.Update(product);

            await _unitOfWork.SaveChangesAsync(ct);
            await StockWorkflow.CommitIfOwnedAsync(_unitOfWork, ownsTransaction, ct);

            return Result<StockDocumentDto>.Success(StockDocumentDto.FromDomain(document));
        }
        catch
        {
            await StockWorkflow.RollbackIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
            throw;
        }
    }
}
