namespace Wizz.Domain;

/// <summary>عملية بيع — تستخدم النواة المالية (SaleCalculator) لحساب الإجماليات.</summary>
public sealed class Sale
{
    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid? CashierId { get; private set; }
    public DateTime SaleDate { get; private set; }
    public SaleStatus Status { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }
    public decimal Paid { get; private set; }
    public decimal Remaining { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }
    public bool IsCreditSale { get; private set; }
    public DateTime? DueDate { get; private set; }

    private readonly List<SaleItem> _items = new();
    public IReadOnlyList<SaleItem> Items => _items.AsReadOnly();

    private Sale() { }

    public Sale(Guid storeId, Guid? cashierId, IEnumerable<SaleItem> items, decimal discount, decimal paid, PaymentMethod paymentMethod, bool isCreditSale = false, DateTime? dueDate = null)
    {
        if (items == null || !items.Any()) throw new ArgumentException("يجب أن يتضمن البيع سطراً واحداً على الأقل", nameof(items));

        Id = Guid.NewGuid();
        StoreId = storeId;
        CashierId = cashierId;
        SaleDate = DateTime.UtcNow;
        Status = SaleStatus.Completed;
        Discount = discount;
        Paid = paid;
        PaymentMethod = paymentMethod;
        IsCreditSale = isCreditSale;
        DueDate = dueDate;

        foreach (var item in items)
        {
            item.AttachToSale(Id);
            _items.Add(item);
        }

        Recalculate();
    }

    public static Sale CreateCreditSale(Guid storeId, Guid? cashierId, IEnumerable<SaleItem> items, decimal discount, DateTime dueDate)
    {
        return new Sale(storeId, cashierId, items, discount, 0m, PaymentMethod.Credit, true, dueDate);
    }

    public void ApplyPayment(decimal amount, PaymentMethod method)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "المبلغ يجب أن يكون أكبر من صفر");
        if (amount > Remaining) throw new ArgumentOutOfRangeException(nameof(amount), "المبلغ يتجاوز المتبقي");

        Paid += amount;
        Remaining -= amount;
        if (PaymentMethod == PaymentMethod.Credit && method != PaymentMethod.Credit)
        {
            PaymentMethod = method;
        }
        if (Remaining == 0)
        {
            IsCreditSale = false;
            DueDate = null;
        }
    }

    /// <summary>تحرير كامل للعملية: استبدال الأصناف وتحديث طرق الدفع والخصم والمبلغ المدفوع وتاريخ الاستحقاق وإعادة حساب الإجماليات وتعديل المخزون حسب الفرق.</summary>
    public void Edit(
        IEnumerable<SaleItem> newItems,
        decimal discount,
        decimal paid,
        PaymentMethod paymentMethod,
        bool isCreditSale,
        DateTime? dueDate)
    {
        if (newItems == null || !newItems.Any()) throw new ArgumentException("يجب أن يتضمن البيع سطراً واحداً على الأقل", nameof(newItems));

        // حساب الكميات القديمة بالوحدة الأساسية لكل منتج (قبل الاستبدال)
        var oldQuantities = _items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Unit!.ToBaseQuantity(i.Quantity)));

        // استبدال الأصناف
        _items.Clear();
        foreach (var item in newItems)
        {
            item.AttachToSale(Id);
            _items.Add(item);
        }

        // حساب الكميات الجديدة بالوحدة الأساسية لكل منتج
        var newQuantities = _items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Unit!.ToBaseQuantity(i.Quantity)));

        // تطبيق فرق المخزون لكل منتج (جديد - قديم)
        // delta > 0 (زيادة الكمية) →خصم من المخزون
        // delta < 0 (نقص الكمية) →إرجاع للمخزون
        foreach (var productId in oldQuantities.Keys.Union(newQuantities.Keys))
        {
            var oldQty = oldQuantities.TryGetValue(productId, out var o) ? o : 0m;
            var newQty = newQuantities.TryGetValue(productId, out var n) ? n : 0m;
            var delta = newQty - oldQty;

            if (delta != 0)
            {
                var item = _items.FirstOrDefault(i => i.ProductId == productId);
                var product = item?.Product;
                if (product != null && !product.IsService)
                {
                    product.ApplyStockChange(-delta);
                }
            }
        }

        // تحديث حقول البيع
        Discount = discount;
        Paid = paid;
        PaymentMethod = paymentMethod;
        IsCreditSale = isCreditSale;
        DueDate = dueDate;

        Recalculate();

        // إذا تجاوز المبلغ المدفوع الإجمالي الجديد، نضبطه على الإجمالي الجديد
        if (Paid > Total)
        {
            Paid = Total;
            Remaining = 0;
        }
        else
        {
            Remaining = Total - Paid;
        }
    }

    public void Cancel(string reason, Guid cancelledByUserId)
    {
        if (Status == SaleStatus.Cancelled) throw new InvalidOperationException("البيع ملغي بالفعل");

        // إرجاع الكميات إلى المخزون عند الإلغاء
        foreach (var item in _items)
        {
            var product = item.Product;
            if (product == null || product.IsService) continue;
            var baseQuantity = item.Unit.ToBaseQuantity(item.Quantity);
            if (baseQuantity == 0) continue;
            product.ApplyStockChange(baseQuantity);
        }

        Status = SaleStatus.Cancelled;
    }

    public void AddItem(SaleItem item)
    {
        if (Status != SaleStatus.Pending) throw new InvalidOperationException("لا يمكن إضافة أسطر لبيع مكتمل");
        item.AttachToSale(Id);
        _items.Add(item);
        Recalculate();
    }

    public void RemoveItem(Guid itemId)
    {
        if (Status != SaleStatus.Pending) throw new InvalidOperationException("لا يمكن حذف أسطر من بيع مكتمل");
        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) throw new ArgumentException("سطر غير موجود", nameof(itemId));
        _items.Remove(item);
        Recalculate();
    }

    private void Recalculate()
    {
        var calcLines = _items.Select(i => i.ToCalculationLine()).ToList();
        var totals = SaleCalculator.Calculate(calcLines, Discount, Paid);
        Subtotal = totals.Subtotal;
        Total = totals.Total;
        Remaining = totals.Remaining;
    }
}

public enum SaleStatus
{
    Pending = 0,
    Completed = 1,
    Corrected = 2,
    Cancelled = 3
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Credit = 2,
    Mobile = 3,
    Mixed = 4
}