namespace Wizz.Domain;

/// <summary>نوع حركة المخزون — يحدد سبب تغيّر الرصيد.</summary>
public enum StockMovementType
{
    OpeningBalance = 0,
    Sale = 1,
    SaleCancellation = 2,
    SaleCorrection = 3,
    ManualAdjustment = 4
}

/// <summary>حركة مخزون — سجل تدقيق لكل تغيير في رصيد منتج، يدوياً أو عبر بيع.</summary>
public sealed class StockMovement
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? SaleId { get; private set; }
    public StockMovementType Type { get; private set; }

    /// <summary>التغيير بالوحدة الأساسية للمنتج: سالب للخروج، موجب للدخول.</summary>
    public decimal QuantityDelta { get; private set; }

    public decimal QuantityBefore { get; private set; }
    public decimal QuantityAfter { get; private set; }
    public string? Reason { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Product? Product { get; private set; }

    private StockMovement() { } // EF Core

    public StockMovement(
        Guid productId,
        StockMovementType type,
        decimal quantityDelta,
        decimal quantityBefore,
        string? reason = null,
        Guid? saleId = null,
        Guid? createdByUserId = null)
    {
        if (quantityDelta == 0 && type != StockMovementType.ManualAdjustment)
            throw new ArgumentOutOfRangeException(nameof(quantityDelta), "حركة المخزون يجب أن تغيّر الرصيد");

        Id = Guid.NewGuid();
        ProductId = productId;
        Type = type;
        QuantityDelta = quantityDelta;
        QuantityBefore = quantityBefore;
        QuantityAfter = quantityBefore + quantityDelta;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SaleId = saleId;
        CreatedByUserId = createdByUserId;
        CreatedAt = DateTime.UtcNow;
    }
}
