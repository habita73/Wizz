namespace Wizz.Domain;

/// <summary>خطأ مجال واضح عندما تتجاوز عملية حساب البيع النطاق المالي المدعوم.</summary>
public sealed class SaleCalculationException : InvalidOperationException
{
    public SaleCalculationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
