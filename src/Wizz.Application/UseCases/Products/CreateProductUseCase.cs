namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;

/// <summary>إنشاء منتج جديد، مع تسجيل الرصيد الافتتاحي كمستند (RSL) لا كرقم منفصل.</summary>
public sealed class CreateProductUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateProductUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<ProductDto>> ExecuteAsync(ProductInputDto input, CancellationToken ct = default)
    {
        // التحقق من الباركود
        if (!string.IsNullOrWhiteSpace(input.Barcode))
        {
            var exists = await _unitOfWork.Products.ExistsBarcodeAsync(input.Barcode, null, ct);
            if (exists)
                return Result<ProductDto>.Failure("باركود مستخدم بالفعل");
        }

        var product = new Product(input.Name, input.RetailPrice, input.WholesalePrice, input.IsService, input.Barcode, input.Description,
            input.StockQuantity, input.ReorderLevel);

        // إضافة الوحدات
        if (input.Units != null && input.Units.Count > 0)
        {
            foreach (var unitInput in input.Units)
            {
                product.AddUnit(unitInput.Name, unitInput.Symbol, unitInput.BaseQuantity);
            }
        }
        else
        {
            // وحدة افتراضية
            product.AddUnit("قطعة", "قطعة", 1m);
        }

        Store? store = null;
        var recordOpening = false;
        if (!product.IsService && product.StockQuantity > 0)
        {
            store = await _unitOfWork.Store.GetAsync(ct);
            if (store == null)
                return Result<ProductDto>.Failure("لم يتم إعداد المتجر بعد — لا يمكن تسجيل رصيد افتتاحي");

            // تعطيل تتبع المخزون يعني عدم إنشاء مستندات؛ يبقى الرصيد مرجعًا إرشاديًا فقط.
            recordOpening = store.InventoryEnabled;
        }

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            await _unitOfWork.Products.AddAsync(product, ct);

            if (recordOpening && store != null)
            {
                var baseUnit = product.GetBaseUnit();
                if (baseUnit == null)
                    throw new InvalidOperationException("المنتج بلا وحدة أساسية");

                var businessDate = StockWorkflow.Today(store);
                var number = await DocumentNumbering.AllocateAsync(
                    _unitOfWork.Db, store.Id, StockDocumentType.OpeningBalance, businessDate,
                    store.DocumentYearDigits, ct);

                var stock = StockQuantity.Normalize(product.StockQuantity);
                var line = new StockDocumentLine(
                    product.Id, baseUnit.Id, stock, stock, 0m, stock, "رصيد افتتاحي عند إنشاء المنتج");
                var document = new StockDocument(
                    store.Id, StockDocumentType.OpeningBalance, number, businessDate, null,
                    "رصيد افتتاحي عند إنشاء المنتج", new[] { line });

                await _unitOfWork.StockDocuments.AddAsync(document, ct);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await StockWorkflow.CommitIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
        }
        catch
        {
            await StockWorkflow.RollbackIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
            throw;
        }

        return Result<ProductDto>.Success(ProductDto.FromDomain(product));
    }
}
