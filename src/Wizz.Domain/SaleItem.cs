namespace Wizz.Domain;

/// <summary>سطر بيع للمجال — يربط منتج، وحدة، كمية، وسعر؛ قابل للتحويل لخط النواة المالية.</summary>
public sealed class SaleItem
{
    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public bool IsWholesalePrice { get; private set; }

    public Product? Product { get; private set; }
    public UnitOfMeasure? Unit { get; private set; }

    private SaleItem() { }

    public SaleItem(Guid productId, Guid unitId, decimal quantity, decimal unitPrice, bool isWholesalePrice)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "الكمية يجب أن تكون أكبر من صفر");
        Money.RequireAtMostTwoDecimalPlaces(unitPrice, nameof(unitPrice));
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "سعر الوحدة لا يمكن أن يكون سالباً");

        Id = Guid.NewGuid();
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        IsWholesalePrice = isWholesalePrice;
        LineTotal = Money.Round(quantity * unitPrice);
    }

    public void UpdateQuantity(decimal newQuantity)
    {
        if (newQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(newQuantity), "الكمية يجب أن تكون أكبر من صفر");
        Quantity = newQuantity;
        LineTotal = Money.Round(Quantity * UnitPrice);
    }

    public void UpdatePrice(decimal newUnitPrice, bool isWholesale)
    {
        Money.RequireAtMostTwoDecimalPlaces(newUnitPrice, nameof(newUnitPrice));
        if (newUnitPrice < 0) throw new ArgumentOutOfRangeException(nameof(newUnitPrice), "سعر الوحدة لا يمكن أن يكون سالباً");
        UnitPrice = newUnitPrice;
        IsWholesalePrice = isWholesale;
        LineTotal = Money.Round(Quantity * UnitPrice);
    }

    /// <summary>ربط الصنف بعملية البيع (يُستخدم من داخل الكيان فقط).</summary>
    internal void AttachToSale(Guid saleId) => SaleId = saleId;

    /// <summary>ربط بيانات الكتالوج لعرض الاسم والوحدة في الإيصال والتقارير.</summary>
    public void SetCatalog(Product product, UnitOfMeasure unit)
    {
        Product = product ?? throw new ArgumentNullException(nameof(product));
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
    }

    /// <summary>تحويل لخط النواة المالية للحساب.</summary>
    public SaleLine ToCalculationLine() => new(UnitPrice, Quantity);
}