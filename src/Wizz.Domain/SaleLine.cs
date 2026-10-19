namespace Wizz.Domain;

/// <summary>خط بيع للنواة المالية — مستقل عن الواجهة أو التخزين أو الطباعة.</summary>
public sealed record SaleLine
{
    public SaleLine(decimal unitPrice, decimal quantity)
    {
        Money.RequireAtMostTwoDecimalPlaces(unitPrice, nameof(unitPrice));

        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "يجب أن يكون سعر الوحدة أكبر من صفر.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "يجب أن تكون الكمية أكبر من صفر.");
        }

        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public decimal UnitPrice { get; }

    public decimal Quantity { get; }

    public decimal LineTotal
    {
        get
        {
            try
            {
                return Money.Round(checked(UnitPrice * Quantity));
            }
            catch (OverflowException exception)
            {
                throw new SaleCalculationException("تجاوز إجمالي سطر البيع النطاق المالي المدعوم.", exception);
            }
        }
    }
}