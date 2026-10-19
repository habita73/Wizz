namespace Wizz.Domain;

/// <summary>
/// يحسب الإجماليات النقدية فقط. الخصم مبلغ ثابت على كامل البيع، لا نسبة مئوية.
/// تقبل مبالغ النقد حتى منزلتين عشريتين فقط؛ وتقرب إجماليات السطور قبل جمعها.
/// لا يقبل الدفع الذي يتجاوز الإجمالي؛ ولا يعالج الآجل أو الإرجاع النقدي.
/// </summary>
public static class SaleCalculator
{
    public static SaleTotals Calculate(IReadOnlyCollection<SaleLine> lines, decimal discount, decimal paid)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("يجب أن يتضمن البيع سطرًا واحدًا على الأقل.", nameof(lines));
        }

        if (lines.Any(line => line is null))
        {
            throw new ArgumentException("لا يمكن أن يتضمن البيع سطرًا فارغًا.", nameof(lines));
        }

        Money.RequireAtMostTwoDecimalPlaces(discount, nameof(discount));
        Money.RequireAtMostTwoDecimalPlaces(paid, nameof(paid));

        if (discount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discount), "لا يمكن أن يكون الخصم سالبًا.");
        }

        if (paid < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paid), "لا يمكن أن يكون المبلغ المدفوع سالبًا.");
        }

        decimal subtotal = 0m;

        try
        {
            foreach (var line in lines)
            {
                subtotal = checked(subtotal + line.LineTotal);
            }
        }
        catch (OverflowException exception)
        {
            throw new SaleCalculationException("تجاوز الإجمالي الفرعي للبيع النطاق المالي المدعوم.", exception);
        }

        var roundedDiscount = discount;

        if (roundedDiscount > subtotal)
        {
            throw new ArgumentOutOfRangeException(nameof(discount), "لا يمكن أن يتجاوز الخصم الإجمالي الفرعي.");
        }

        var total = Money.Round(subtotal - roundedDiscount);
        var roundedPaid = paid;

        if (roundedPaid > total)
        {
            throw new ArgumentOutOfRangeException(nameof(paid), "لا يمكن أن يتجاوز المدفوع إجمالي البيع.");
        }

        return new SaleTotals(
            subtotal,
            roundedDiscount,
            total,
            roundedPaid,
            Money.Round(total - roundedPaid));
    }
}
