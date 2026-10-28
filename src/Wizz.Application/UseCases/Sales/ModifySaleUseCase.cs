namespace Wizz.Application.UseCases.Sales;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>تحرير كامل لعملية بيع موجودة: استبدال الأصناف وتحديث طرق الدفع والخصم والمبلغ المدفوع وتاريخ الاستحقاق وتعديل المخزون حسب الفرق.</summary>
public sealed class ModifySaleUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public ModifySaleUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SaleDto>> ExecuteAsync(
        Guid saleId,
        SaleInputDto input,
        CancellationToken ct = default)
    {
        if (input.Lines == null || input.Lines.Count == 0)
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

        // معرّفات الأصناف القديمة للحذف
        var oldItemIds = sale.Items.Select(i => i.Id).ToList();

        // بناء الأصناف الجديدة مع تحميل المنتجات والوحدات
        var newItems = new List<SaleItem>();

        foreach (var lineInput in input.Lines)
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

            var price = lineInput.UnitPrice ?? product.GetPrice(lineInput.UseWholesalePrice);
            var item = new SaleItem(lineInput.ProductId, lineInput.UnitId, lineInput.Quantity, price, lineInput.UseWholesalePrice);
            item.SetCatalog(product, unit);
            newItems.Add(item);
        }

        // تحديث حقول البيع في الكيان في الذاكرة (sale.Edit() يعالج修正 المخزون داخلياً بناءً على فرق الكميات)
        sale.Edit(newItems, input.Discount, input.Paid, (PaymentMethod)input.PaymentMethod, input.IsCreditSale, input.DueDate);

        // تحديث قاعدة البيانات مباشرة باستخدام ADO.NET لتجنب مشاكل Change Tracker
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
                                          ""PaymentMethod"" = @paymentMethod, ""IsCreditSale"" = @isCreditSale, ""DueDate"" = @dueDate
                                      WHERE ""Id"" = @saleId";
            var psubtotal = updateCmd.CreateParameter(); psubtotal.ParameterName = "@subtotal"; psubtotal.Value = sale.Subtotal;
            var pdiscount = updateCmd.CreateParameter(); pdiscount.ParameterName = "@discount"; pdiscount.Value = sale.Discount;
            var ptotal = updateCmd.CreateParameter(); ptotal.ParameterName = "@total"; ptotal.Value = sale.Total;
            var ppaid = updateCmd.CreateParameter(); ppaid.ParameterName = "@paid"; ppaid.Value = sale.Paid;
            var premaining = updateCmd.CreateParameter(); premaining.ParameterName = "@remaining"; premaining.Value = sale.Remaining;
            var ppaymentMethod = updateCmd.CreateParameter(); ppaymentMethod.ParameterName = "@paymentMethod"; ppaymentMethod.Value = (int)sale.PaymentMethod;
            var pisCreditSale = updateCmd.CreateParameter(); pisCreditSale.ParameterName = "@isCreditSale"; pisCreditSale.Value = sale.IsCreditSale;
            var pdueDate = updateCmd.CreateParameter(); pdueDate.ParameterName = "@dueDate"; pdueDate.Value = (object?)sale.DueDate ?? DBNull.Value;
            var psaleId = updateCmd.CreateParameter(); psaleId.ParameterName = "@saleId"; psaleId.Value = saleId;
            updateCmd.Parameters.Add(psubtotal);
            updateCmd.Parameters.Add(pdiscount);
            updateCmd.Parameters.Add(ptotal);
            updateCmd.Parameters.Add(ppaid);
            updateCmd.Parameters.Add(premaining);
            updateCmd.Parameters.Add(ppaymentMethod);
            updateCmd.Parameters.Add(pisCreditSale);
            updateCmd.Parameters.Add(pdueDate);
            updateCmd.Parameters.Add(psaleId);
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
        sale.CashierId, null);
}
