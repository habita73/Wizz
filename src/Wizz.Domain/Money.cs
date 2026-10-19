namespace Wizz.Domain;

internal static class Money
{
    public static void RequireAtMostTwoDecimalPlaces(decimal amount, string parameterName)
    {
        if (decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "يجب ألا يحتوي المبلغ النقدي على أكثر من منزلتين عشريتين.");
        }
    }

    /// <summary>
    /// قاعدة هذه النواة: كل مبلغ نقدي نهائي يقرب إلى منزلتين عشريتين
    /// باستخدام MidpointRounding.AwayFromZero.
    /// </summary>
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
