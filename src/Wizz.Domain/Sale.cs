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

    public bool IsCorrected { get; private set; }
    public Guid? OriginalSaleId { get; private set; }
    public string? CorrectionReason { get; private set; }

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

    public Sale Correct(string reason, Guid correctedByUserId)
    {
        if (IsCorrected) throw new InvalidOperationException("هذا البيع مصحح بالفعل");
        if (Status == SaleStatus.Cancelled) throw new InvalidOperationException("لا يمكن تصحيح بيع ملغي");

        var correctedItems = _items.Select(i => new SaleItem(i.ProductId, i.UnitId, i.Quantity, i.UnitPrice, i.IsWholesalePrice)).ToList();
        var correctedSale = new Sale(StoreId, correctedByUserId, correctedItems, Discount, Paid, PaymentMethod, IsCreditSale, DueDate)
        {
            OriginalSaleId = Id,
            CorrectionReason = reason,
            IsCorrected = true
        };

        Status = SaleStatus.Corrected;
        return correctedSale;
    }

    public void Cancel(string reason)
    {
        if (Status == SaleStatus.Cancelled) throw new InvalidOperationException("البيع ملغي بالفعل");
        if (IsCorrected) throw new InvalidOperationException("لا يمكن إلغاء بيع مصحح — ألغِ التصحيح أولاً");

        Status = SaleStatus.Cancelled;
        CorrectionReason = reason;
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