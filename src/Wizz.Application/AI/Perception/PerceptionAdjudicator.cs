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
    public const string ApproximateNameAcceptedReason =
        "اعتمده المدمج: الاسم مطابق تقريبًا لبيانات المتجر والسعر مطابق";
    public const string UnknownProductReason = "رفضه المدمج: اسم المنتج غير معروف في بيانات المتجر";
    public const string MissingPriceReason = "رفضه المدمج: لا يوجد سعر مقروء في المرشّح";
    public const string PriceMismatchReason = "رفضه المدمج: السعر غير مطابق لبيانات المتجر";

    private const decimal PriceTolerance = 0.01m;

    // أوصاف معروفة يُسمح أن تزيد في اسم المرشّح على اسم المنتج المسجّل. أي كلمة أخرى تمنع
    // التطابق التقريبي، حتى لا يُقبل «حليب مملّح» مثلًا بوصفه «حليب».
    private static readonly string[] KnownDescriptors =
    {
        "معقم", "طازج", "كامل", "دسم", "الدسم", "احمر", "اخضر", "ابيض", "اسود", "بني",
        "مطحون", "مطحونه", "مطحونة", "حبه", "علبه", "كغ", "كيلو", "قطعه", "صغير", "كبير", "جديد"
    };

    private static readonly char[] TokenSeparators =
    {
        ' ', '\t', '-', '|', '/', ',', '.', '(', ')', '؛', ':'
    };

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
        var exact = knownProducts.FirstOrDefault(kvp =>
            string.Equals(TextNormalizer.Normalize(kvp.Key), normalizedName, StringComparison.Ordinal));

        if (exact.Value is not null)
            return JudgePrice(candidate, exact.Value, AcceptedReason);

        // التطابق التقريبي مقيد: تطابق تام بمجموعة الكلمات، أو كلمة/كلمات وصفية معروفة تزيد على الاسم.
        // إن طابق المرشّح أكثر من منتج مختلف فلا يُختار أحدها؛ يُرفض بوصفه غير معروف بدل التخمين.
        var nearMatches = knownProducts
            .Where(kvp => IsApproximateName(normalizedName, TextNormalizer.Normalize(kvp.Key)))
            .ToList();

        if (nearMatches.Count == 0
            || nearMatches.Select(kvp => TextNormalizer.Normalize(kvp.Key)).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            return (false, UnknownProductReason);
        }

        return JudgePrice(candidate, nearMatches[0].Value, ApproximateNameAcceptedReason);
    }

    private static (bool, string) JudgePrice(
        PerceptionCandidate candidate,
        IReadOnlyList<decimal> allowedPrices,
        string acceptedReason)
    {
        if (candidate.Price is not { } price)
            return (false, MissingPriceReason);

        return allowedPrices.Any(allowed => Math.Abs(allowed - price) <= PriceTolerance)
            ? (true, acceptedReason)
            : (false, PriceMismatchReason);
    }

    private static bool IsApproximateName(string candidateName, string knownName)
    {
        var candidateTokens = NameTokens(candidateName);
        var knownTokens = NameTokens(knownName);
        if (candidateTokens.Count == 0 || knownTokens.Count == 0) return false;
        if (candidateTokens.SetEquals(knownTokens)) return true;

        var shorter = candidateTokens.Count <= knownTokens.Count ? candidateTokens : knownTokens;
        var longer = ReferenceEquals(shorter, candidateTokens) ? knownTokens : candidateTokens;
        if (!shorter.IsSubsetOf(longer)) return false;

        return longer.Except(shorter).All(token => KnownDescriptors.Contains(token, StringComparer.Ordinal));
    }

    private static HashSet<string> NameTokens(string normalizedName) =>
        normalizedName
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2)
            .ToHashSet(StringComparer.Ordinal);
}
