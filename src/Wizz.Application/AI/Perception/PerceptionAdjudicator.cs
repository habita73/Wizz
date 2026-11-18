namespace Wizz.Application.AI.Perception;

/// <summary>
/// حارس التعرّف النهائي: المدمج وحده يقرّر أي مرشّح يُعتمد، بمقارنته بحقائق منتجات
/// مُستخرَجة من استعلامات محددة (لا من توليد النص). لا يكتب في قاعدة البيانات،
/// ولا يفرّق بين مرشّح محلي وسحابي في الحكم — المصدر لا يمنح ثقة إضافية.
/// </summary>
public static class PerceptionAdjudicator
{
    public const string NoWriteReason = "لا كتابة تلقائية في قاعدة البيانات — يُعرض المرشّح المعتمد فقط بعد تأكيد المستخدم";
    public const string AcceptedReason = "اعتمده المدمج: الاسم والسعر مطابقان لبيانات المتجر";
    public const string UnknownProductReason = "رفضه المدمج: اسم المنتج غير معروف في بيانات المتجر";
    public const string MissingPriceReason = "رفضه المدمج: لا يوجد سعر مقروء في المرشّح";
    public const string PriceMismatchReason = "رفضه المدمج: السعر غير مطابق لبيانات المتجر";

    private const decimal PriceTolerance = 0.01m;

    /// <summary>
    /// يحكّم دفعة المرشّحات مقابل خريطة «اسم المنتج → الأسعار المعتمدة» التي يبنيها المدمج.
    /// </summary>
    public static PerceptionResult Adjudicate(
        IReadOnlyList<PerceptionCandidate> candidates,
        IReadOnlyDictionary<string, IReadOnlyList<decimal>> knownProducts)
    {
        var verdicts = new List<AdjudicatedCandidate>(candidates.Count);
        var accepted = 0;

        foreach (var candidate in candidates)
        {
            var (ok, reason) = Judge(candidate, knownProducts);
            if (ok) accepted++;
            verdicts.Add(new AdjudicatedCandidate(candidate, ok, reason));
        }

        var rejected = verdicts.Count - accepted;
        var summary = $"تحكيم المدمج: {accepted} مقبول، {rejected} مرفوض. {NoWriteReason}";
        return new PerceptionResult(verdicts, accepted, rejected, summary, WrittenToDatabase: false);
    }

    private static (bool, string) Judge(
        PerceptionCandidate candidate,
        IReadOnlyDictionary<string, IReadOnlyList<decimal>> knownProducts)
    {
        if (string.IsNullOrWhiteSpace(candidate.ProductName))
            return (false, UnknownProductReason);

        var normalizedName = TextNormalizer.Normalize(candidate.ProductName);
        var match = knownProducts.FirstOrDefault(kvp =>
            string.Equals(TextNormalizer.Normalize(kvp.Key), normalizedName, StringComparison.Ordinal));

        if (match.Value is null)
            return (false, UnknownProductReason);

        if (candidate.Price is not { } price)
            return (false, MissingPriceReason);

        var priceOk = match.Value.Any(allowed => Math.Abs(allowed - price) <= PriceTolerance);
        return priceOk
            ? (true, AcceptedReason)
            : (false, PriceMismatchReason);
    }
}
