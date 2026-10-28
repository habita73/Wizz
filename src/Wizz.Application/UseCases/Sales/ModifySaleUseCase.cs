namespace Wizz.Application.UseCases.Sales;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>تعديل بيع مباشر على العملية نفسها مع تعديل المخزون حسب الفرق وإضافة ملاحظة التعديل.</summary>
public sealed class ModifySaleUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public ModifySaleUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SaleDto>> ExecuteAsync(
        Guid saleId,
        IReadOnlyList<SaleLineInputDto> newLines,
        string modificationNote,
        Guid modifiedByUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modificationNote))
        {
            return Result<SaleDto>.Failure("ملاحظة التعديل مطلوبة");
        }

        if (newLines == null || newLines.Count == 0)
        {
            return Result<SaleDto>.Failure("يجب أن يتضمن البيع سطراً واحداً على الأقل");
        }

        // تحميل البيع بدون تتبع (لتجنب تعارض Change Tracker مع SQL المباشر)
        var sale = await _unitOfWork.Sales.GetByIdAsyncNoTracking(saleId, ct);
        if (sale is null)
        {
            return Result<SaleDto>.Failure("لم يتم العثور على البيع");
        }

        if (sale.Status == SaleStatus.Cancelled)
        {
            return Result<SaleDto>.Failure("لا يمكن تعديل بيع ملغي");
        }

        // حساب الكميات القديمة بالوحدة الأساسية لكل منتج
        var oldQuantities = sale.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Unit!.ToBaseQuantity(i.Quantity)));

        // معرّفات الأصناف القديمة للحذف
        var oldItemIds = sale.Items.Select(i => i.Id).ToList();

        // بناء الأصناف الجديدة
        var newItems = new List<SaleItem>();
        var unitBaseQuantities = new Dictionary<(Guid ProductId, Guid UnitId), decimal>();

        foreach (var lineInput in newLines)
        {
            var product = await _unitOfWork.Products.GetByIdAsync(lineInput.ProductId, ct);
            if (product == null)
            {
                return Result<SaleDto>.Failure($"منتج غير موجود: {lineInput.ProductId}");
            }

            if (!product.IsActive)
            {
                return Result<SaleDto>.Failure($"منتج غير نشط: {product.Name}");
            }

            var unit = product.GetUnitById(lineInput.UnitId);
            if (unit == null)
            {
                return Result<SaleDto>.Failure($"وحدة غير موجودة للمنتج: {product.Name}");
            }

            unitBaseQuantities[(lineInput.ProductId, lineInput.UnitId)] = unit.BaseQuantity;

            var price = product.GetPrice(lineInput.UseWholesalePrice);
            var item = new SaleItem(lineInput.ProductId, lineInput.UnitId, lineInput.Quantity, price, lineInput.UseWholesalePrice);
            newItems.Add(item);
        }

        // حساب الكميات الجديدة بالوحدة الأساسية لكل منتج
        var newQuantities = newItems
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i =>
            {
                var baseQty = unitBaseQuantities[(i.ProductId, i.UnitId)];
                return baseQty * i.Quantity;
            }));

        // تطبيق فرق المخزون لكل منتج (جديد - قديم)
        foreach (var productId in oldQuantities.Keys.Union(newQuantities.Keys))
        {
            var oldQty = oldQuantities.TryGetValue(productId, out var o) ? o : 0m;
            var newQty = newQuantities.TryGetValue(productId, out var n) ? n : 0m;
            var delta = newQty - oldQty;

            if (delta != 0)
            {
                var product = await _unitOfWork.Products.GetByIdAsync(productId, ct);
                if (product != null && !product.IsService)
                {
                    product.ApplyStockChange(-delta);
                }
            }
        }

        // استبدال الأصناف وتحديد علامات التعديل
        sale.ReplaceItems(newItems);
        sale.MarkModified(modificationNote.Trim(), modifiedByUserId);

        // إذا تجاوز المبلغ المدفوع الإجمالي الجديد، نضبطه على الإجمالي الجديد
        if (sale.Paid > sale.Total)
        {
            sale.AdjustPaid(sale.Total);
        }

        // تحديث البيع والأصناف باستخدام ADO.NET مباشر لتجنب مشاكل Change Tracker
        var db = _unitOfWork.Db;
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        using var transaction = connection.BeginTransaction();

        try
        {
            // حذف الأصناف القديمة
            foreach (var oldItemId in oldItemIds)
            {
                using var deleteCmd = connection.CreateCommand();
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM \"SaleItems\" WHERE \"Id\" = @id";
                var p = deleteCmd.CreateParameter();
                p.ParameterName = "@id";
                p.Value = oldItemId;
                deleteCmd.Parameters.Add(p);
                await deleteCmd.ExecuteNonQueryAsync(ct);
            }

            // إدخال الأصناف الجديدة
            foreach (var item in newItems)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"INSERT INTO ""SaleItems"" (""Id"", ""SaleId"", ""ProductId"", ""UnitId"", ""Quantity"", ""UnitPrice"", ""IsWholesalePrice"", ""LineTotal"")
                                          VALUES (@id, @saleId, @productId, @unitId, @quantity, @unitPrice, @isWholesale, @lineTotal)";
                var pid = insertCmd.CreateParameter(); pid.ParameterName = "@id"; pid.Value = item.Id;
                var psid = insertCmd.CreateParameter(); psid.ParameterName = "@saleId"; psid.Value = saleId;
                var ppid = insertCmd.CreateParameter(); ppid.ParameterName = "@productId"; ppid.Value = item.ProductId;
                var puid = insertCmd.CreateParameter(); puid.ParameterName = "@unitId"; puid.Value = item.UnitId;
                var pq = insertCmd.CreateParameter(); pq.ParameterName = "@quantity"; pq.Value = item.Quantity;
                var pupr = insertCmd.CreateParameter(); pupr.ParameterName = "@unitPrice"; pupr.Value = item.UnitPrice;
                var piw = insertCmd.CreateParameter(); piw.ParameterName = "@isWholesale"; piw.Value = item.IsWholesalePrice;
                var plt = insertCmd.CreateParameter(); plt.ParameterName = "@lineTotal"; plt.Value = Math.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
                insertCmd.Parameters.Add(pid);
                insertCmd.Parameters.Add(psid);
                insertCmd.Parameters.Add(ppid);
                insertCmd.Parameters.Add(puid);
                insertCmd.Parameters.Add(pq);
                insertCmd.Parameters.Add(pupr);
                insertCmd.Parameters.Add(piw);
                insertCmd.Parameters.Add(plt);
                await insertCmd.ExecuteNonQueryAsync(ct);
            }

            // تحديث البيع
            using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = @"UPDATE ""Sales""
                                      SET ""Subtotal"" = @subtotal, ""Discount"" = @discount, ""Total"" = @total, ""Paid"" = @paid, ""Remaining"" = @remaining,
                                          ""IsModified"" = @isModified, ""ModifiedAt"" = @modifiedAt, ""ModifiedByUserId"" = @modifiedByUserId, ""ModificationNote"" = @modificationNote
                                      WHERE ""Id"" = @saleId";
            var psubtotal = updateCmd.CreateParameter(); psubtotal.ParameterName = "@subtotal"; psubtotal.Value = sale.Subtotal;
            var pdiscount = updateCmd.CreateParameter(); pdiscount.ParameterName = "@discount"; pdiscount.Value = sale.Discount;
            var ptotal = updateCmd.CreateParameter(); ptotal.ParameterName = "@total"; ptotal.Value = sale.Total;
            var ppaid = updateCmd.CreateParameter(); ppaid.ParameterName = "@paid"; ppaid.Value = sale.Paid;
            var premaining = updateCmd.CreateParameter(); premaining.ParameterName = "@remaining"; premaining.Value = sale.Remaining;
            var pismodified = updateCmd.CreateParameter(); pismodified.ParameterName = "@isModified"; pismodified.Value = sale.IsModified;
            var pmodifiedat = updateCmd.CreateParameter(); pmodifiedat.ParameterName = "@modifiedAt"; pmodifiedat.Value = sale.ModifiedAt;
            var pmodifiedby = updateCmd.CreateParameter(); pmodifiedby.ParameterName = "@modifiedByUserId"; pmodifiedby.Value = sale.ModifiedByUserId;
            var pnote = updateCmd.CreateParameter(); pnote.ParameterName = "@modificationNote"; pnote.Value = (object)sale.ModificationNote ?? DBNull.Value;
            var psaleid = updateCmd.CreateParameter(); psaleid.ParameterName = "@saleId"; psaleid.Value = saleId;
            updateCmd.Parameters.Add(psubtotal);
            updateCmd.Parameters.Add(pdiscount);
            updateCmd.Parameters.Add(ptotal);
            updateCmd.Parameters.Add(ppaid);
            updateCmd.Parameters.Add(premaining);
            updateCmd.Parameters.Add(pismodified);
            updateCmd.Parameters.Add(pmodifiedat);
            updateCmd.Parameters.Add(pmodifiedby);
            updateCmd.Parameters.Add(pnote);
            updateCmd.Parameters.Add(psaleid);
            await updateCmd.ExecuteNonQueryAsync(ct);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // إعادة تحميل البيع مع الأصناف لـ DTO
        var savedSale = await _unitOfWork.Sales.GetByIdAsync(saleId, ct);

        return Result<SaleDto>.Success(MapToDto(savedSale));
    }

    private static SaleDto MapToDto(Sale sale) => new(
        sale.Id,
        sale.SaleDate,
        (SaleStatusDto)sale.Status,
        sale.Items.Select(i => new SaleLineDto(
            i.Id, i.ProductId, i.Product?.Name ?? string.Empty, i.Product?.Barcode,
            i.UnitId, i.Unit?.Name ?? string.Empty, i.Unit?.Symbol ?? string.Empty,
            i.Quantity, i.UnitPrice, i.LineTotal, i.IsWholesalePrice)).ToList(),
        sale.Subtotal, sale.Discount, sale.Total, sale.Paid, sale.Remaining,
        (PaymentMethodDto)sale.PaymentMethod, sale.IsCreditSale, sale.DueDate,
        sale.CashierId, null, sale.IsModified, sale.ModifiedAt, sale.ModifiedByUserId, sale.ModificationNote);
}
