namespace Wizz.Domain;

/// <summary>
/// قاعدة التقريب الواحدة لكميات المخزون بالوحدة الأساسية.
/// تُستخدم في كل تحويل بين وحدة الإدخال والوحدة الأساسية حتى لا تتراكم فروق غير ثابتة.
/// </summary>
public static class StockQuantity
{
    public const int Scale = 4;

    public static decimal Normalize(decimal value)
        => Math.Round(value, Scale, MidpointRounding.AwayFromZero);

    public static decimal ToBase(UnitOfMeasure unit, decimal quantity)
        => Normalize(unit.ToBaseQuantity(quantity));
}
