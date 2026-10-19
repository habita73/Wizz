namespace Wizz.Domain;

/// <summary>وحدة قياس — تدعم التحويل بين الوحدات (كيس ↔ كجم، علبة ↔ قطعة، إلخ).</summary>
public sealed class UnitOfMeasure
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;          // "كجم"، "كيس"، "قطعة"
    public string Symbol { get; private set; } = default!;        // "kg"، "bag"، "pcs"
    public decimal BaseQuantity { get; private set; }             // الكمية في الوحدة الأساسية (مثال: 1 كيس = 25 كجم)
    public bool IsBaseUnit { get; private set; }                  // هل هي الوحدة الأساسية للمنتج
    public Guid ProductId { get; private set; }

    private UnitOfMeasure() { } // EF Core

    public UnitOfMeasure(string name, string symbol, decimal baseQuantity, bool isBaseUnit = false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم الوحدة مطلوب", nameof(name));
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("رمز الوحدة مطلوب", nameof(symbol));
        if (baseQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(baseQuantity), "الكمية الأساسية يجب أن تكون أكبر من صفر");

        Id = Guid.NewGuid();
        Name = name.Trim();
        Symbol = symbol.Trim();
        BaseQuantity = baseQuantity;
        IsBaseUnit = isBaseUnit;
    }

    public void SetAsBaseUnit() => IsBaseUnit = true;
    public void Update(string name, string symbol, decimal baseQuantity)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم الوحدة مطلوب", nameof(name));
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("رمز الوحدة مطلوب", nameof(symbol));
        if (baseQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(baseQuantity), "الكمية الأساسية يجب أن تكون أكبر من صفر");

        Name = name.Trim();
        Symbol = symbol.Trim();
        BaseQuantity = baseQuantity;
    }

    /// <summary>تحويل كمية من هذه الوحدة إلى الوحدة الأساسية للمنتج.</summary>
    public decimal ToBaseQuantity(decimal quantity) => quantity * BaseQuantity;

    /// <summary>تحويل كمية من الوحدة الأساسية إلى هذه الوحدة.</summary>
    public decimal FromBaseQuantity(decimal baseQuantity) => baseQuantity / BaseQuantity;
}