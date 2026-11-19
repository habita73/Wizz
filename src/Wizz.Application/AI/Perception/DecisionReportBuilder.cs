namespace Wizz.Application.AI.Perception;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>نوع الوثيقة المقدَّر من ترويسة النص فقط؛ تقديري ولا يُعدّ حكمًا من المحكّم.</summary>
public enum EstimatedDocumentType
{
    /// <summary>لا قرينة كافية أو قرائن متعارضة.</summary>
    Unspecified = 0,

    /// <summary>وصل بيع.</summary>
    SaleReceipt,

    /// <summary>فاتورة.</summary>
    Invoice,

    /// <summary>كشف حساب.</summary>
    AccountStatement
}

/// <summary>جزء الوثيقة المقدَّر حسب موضع السطر؛ للعرض فقط ولا يُسقط أي سطر من التحكيم.</summary>
public enum EstimatedDocumentPart
{
    /// <summary>قبل أول صف صنف.</summary>
    Header = 0,

    /// <summary>صف صنف قوبل بمرشّح.</summary>
    Content,

    /// <summary>بعد آخر صف صنف.</summary>
    Footer,

    /// <summary>سطر بين صفوف الأصناف لم يُحكَّم عليه كصنف.</summary>
    Unclassified
}

/// <summary>فئة الصنف؛ تُشتقّ من سبب المحكّم المدمج حرفيًا، لا من تحليل مستقل.</summary>
public enum GoodsDecisionCategory
{
    /// <summary>اسم مسجّل وبياناته مطابقة.</summary>
    Registered = 0,

    /// <summary>الاسم غير معروف في بيانات المتجر.</summary>
    Unknown,

    /// <summary>الاسم معروف لكن بياناته لا تطابق المسجّل (سعر مختلف أو غير مقروء).</summary>
    Conflicting
}

/// <summary>
/// معرفة الاسم بسجل المنتجات؛ تُشتقّ من نص سبب المحكّم نفسه، ولا تعني توفر كمية في المخزون.
/// </summary>
public enum StoreNameKnowledge
{
    /// <summary>السبب يفيد بأن الاسم مطابق، أو أن السعر غير مطابق مع بقاء الاسم معروفًا.</summary>
    Known = 0,

    /// <summary>السبب يقول صراحة إن الاسم غير معروف في بيانات المتجر.</summary>
    Unknown,

    /// <summary>لا يمكن تحديده من السبب (لا يوجد سعر مقروء). لا يُخمَّن.</summary>
    NotDetermined
}

/// <summary>سطر من النص المقروء مع جزئه المقدَّر.</summary>
/// <param name="Text">النص كما قرأه التطبيق.</param>
/// <param name="Part">الجزء المقدَّر حسب الموضع.</param>
public sealed record DecisionReportPartLine(string Text, EstimatedDocumentPart Part);

/// <summary>
/// مرشّح معروض داخل سطر مُعاد تركيبها؛ كل حقل منقول من نتيجة المحكّم، مع دمج المرشّحات المطابقة.
/// </summary>
/// <param name="Name">الاسم كما قرأه التطبيق.</param>
/// <param name="Price">السعر كما قرأه التطبيق أو «—».</param>
/// <param name="Verdict">سبب المحكّم كما صدر، دون تعديل.</param>
/// <param name="Category">الفئة المشتقّة من السبب.</param>
/// <param name="StoreKnowledge">معرفة الاسم بالسجل، المشتقّة من السبب.</param>
/// <param name="Accepted">حكم المحكّم.</param>
/// <param name="SourceLine">السطر المُعاد تركيبها الذي جاء منه المرشّح.</param>
/// <param name="CandidateCount">عدد مرشّحات المدمج المدمجة في هذا الصف.</param>
/// <param name="NearKnownNames">أسماء معروفة قريبة نصيًا؛ تلميح للمراجعة لا حكم إضافي.</param>
public sealed record DecisionReportGood(
    string Name,
    string Price,
    string Verdict,
    GoodsDecisionCategory Category,
    StoreNameKnowledge StoreKnowledge,
    bool Accepted,
    string SourceLine,
    int CandidateCount,
    IReadOnlyList<string> NearKnownNames);

/// <summary>تقرير قرار للعرض فقط: ناقل لأحكام المحكّم، لا حاكم جديد.</summary>
/// <param name="DocumentType">نوع الوثيقة المقدَّر.</param>
/// <param name="PartLines">أسطر النص المُعاد تركيبها مع أجزائها المقدَّرة.</param>
/// <param name="Goods">مرشّحات صفوف المحتوى فقط، مجمّعة حسب السطر.</param>
/// <param name="CandidateTotal">عدد كل مرشّحات المدمج قبل أي تجميع.</param>
/// <param name="MetadataCandidateCount">مرشّحات أسطر الرأس والذيل؛ أحكامها باقية في العدد الكلي.</param>
/// <param name="RegisteredCount">عدد أصناف «مسجّلة معروفة» في صفوف المحتوى.</param>
/// <param name="UnknownCount">عدد أصناف «مجهولة» في صفوف المحتوى.</param>
/// <param name="ConflictingCount">عدد أصناف «مخالفة» في صفوف المحتوى.</param>
/// <param name="AcceptedCount">عدد المقبول كما صدر عن المحكّم.</param>
/// <param name="RejectedCount">عدد المرفوض كما صدر عن المحكّم.</param>
/// <param name="NumericHints">احتمالات تصحيح الأرقام للمراجعة فقط؛ لم تدخل التحكيم ولا تغيّر النص.</param>
/// <param name="WrittenToDatabase">يبقى <c>false</c> دائمًا.</param>
public sealed record DecisionReport(
    EstimatedDocumentType DocumentType,
    IReadOnlyList<DecisionReportPartLine> PartLines,
    IReadOnlyList<DecisionReportGood> Goods,
    int CandidateTotal,
    int MetadataCandidateCount,
    int RegisteredCount,
    int UnknownCount,
    int ConflictingCount,
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<OcrNumericCleanup.Hint> NumericHints,
    bool WrittenToDatabase);

/// <summary>
/// يبني تقرير القرار من نتيجة المحكّم المدمج ومن النص المقروء فقط. لا يقرأ قاعدة بيانات،
/// لا يرسل، لا يحسب أسعارًا، ولا يخترع حكمًا؛ كل ما يفعله هو تجميع الأحكام وعرضها.
/// </summary>
public static class DecisionReportBuilder
{
    /// <summary>الوسم الذي يُلصق بنوع الوثيقة وتقسيم أجزائها.</summary>
    public const string EstimatedLabel = "تقديري";

    /// <summary>يُستخدم حين لا يتوفر اسم أو سعر مقروء.</summary>
    public const string MissingValue = "—";

    /// <summary>توضيح حدود «متوفر» في هذا التقرير.</summary>
    public const string StockNote =
        "«معروف في بيانات المتجر» يعني أن الاسم موجود في سجل المنتجات، ولا يعني توفر كمية في المخزون.";

    /// <summary>توضيح حدود مطابقة الأسماء بعد إذن المطابقة التقريبية المقيدة.</summary>
    public const string NameMatchingNote =
        "المطابقة الحالية للأسماء حرفية بعد التطبيع، ثم تقريبية مقيدة بترتيب الكلمات وأوصاف معروفة؛ " +
        "يُعرض سبب المدمج كما صدر، وراجع الاسم يدويًا قبل أي إضافة.";

    /// <summary>الفرق بين تلميح «قريب من» والقبول التقريبي الصادر عن المدمج.</summary>
    public const string NearNameNote =
        "تلميح «قريب من» للمراجعة فقط؛ لا يضيف قبولًا ولا رفضًا. أما القبول التقريبي فصادر عن المدمج ويظهر سببه في الجدول.";

    // قرينة تقريبية لصف صنف: رقم بحجم سعر (180، 12.5، 1.000) لا ضمن رقم طويل كالتاريخ أو الهاتف.
    private static readonly Regex PriceLikeToken = new(@"(?<!\d)\d{1,3}(?:[.,]\d{1,3})?(?!\d)", RegexOptions.Compiled);

    // كلمات تدل على أن السطر بيانات وثيقة (مجموع، ضريبة، دفع...) لا صف صنف، حتى لو حوى رقمًا عشريًا.
    private static readonly string[] MetadataMarkers =
    {
        "مجموع", "المحموع", "المحصوع", "الكلي", "الاجمالي", "الإجمالي",
        "ضريبه", "tva", "المدفوع", "المبلغ", "البقي", "الباقي",
        "شكرا", "الهاتف", "هاتف", "التاريخ", "تاريخ", "الوقت", "رقم", "فاتور",
        "بطاقه", "دفع", "الرصيد", "العنوان", "عنوان", "السجل", "الرخصه", "الفرع", "العميل",
        "ماي", "جانفي", "فيفري", "مارس", "افريل", "جوان", "جويلية", "اوت", "سبتمبر", "اكتوبر", "نوفمبر", "ديسمبر"
    };

    /// <summary>يبني التقرير؛ لا تعديل على المدخلات ولا تأثير لها خارج العرض.</summary>
    public static DecisionReport Build(string? recognizedText, PerceptionResult adjudication)
    {
        ArgumentNullException.ThrowIfNull(adjudication);

        var lines = (recognizedText ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        // لا يُستخدم وجود مرشّح قرينةً على «صف صنف»: المستخرج يُنتج مرشّحات لأسطر الرأس
        // والتذييل أيضًا. القرينة التقديرية هي رقم بحجم سعر مع خلوّ السطر من كلمات بيانات الوثيقة
        // (مجموع، ضريبة، دفع...)؛ ثم يُحدد الرأس والذيل بالموضع. هذا كله تقريبي وموسوم كذلك.
        var normalizedLines = lines.Select(TextNormalizer.Normalize).ToList();
        var contentIndexes = lines
            .Select((line, index) => (line, index))
            .Where(item => PriceLikeToken.IsMatch(item.line)
                           && !MetadataMarkers.Any(marker =>
                               normalizedLines[item.index].Contains(marker, StringComparison.Ordinal)))
            .Select(item => item.index)
            .ToList();

        var partLines = BuildParts(lines, contentIndexes);
        var documentType = DetectDocumentType(partLines, lines);

        // يربط كل مرشّح بالسطر المُعاد تركيبها الذي أنتجه، فيُفصل «المنتجات» عن بيانات الوثيقة.
        var lineIndexByText = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            var key = TextNormalizer.Normalize(lines[i]);
            if (key.Length > 0 && !lineIndexByText.ContainsKey(key))
            {
                lineIndexByText[key] = i;
            }
        }

        var goods = BuildGoods(adjudication, partLines, lineIndexByText);
        var goodsCandidateCount = goods.Sum(item => item.CandidateCount);

        // احتمالات رقمية للمراجعة فقط، مشتقّة من النص المقروء كما هو؛ لا تُمرَّر للمحكّم.
        var numericHints = OcrNumericCleanup.Analyze(recognizedText).Hints;

        return new DecisionReport(
            documentType,
            partLines,
            goods,
            adjudication.Candidates.Count,
            adjudication.Candidates.Count - goodsCandidateCount,
            goods.Count(item => item.Category == GoodsDecisionCategory.Registered),
            goods.Count(item => item.Category == GoodsDecisionCategory.Unknown),
            goods.Count(item => item.Category == GoodsDecisionCategory.Conflicting),
            adjudication.AcceptedCount,
            adjudication.RejectedCount,
            numericHints,
            adjudication.WrittenToDatabase);
    }

    /// <summary>الاسم المعروض لنوع الوثيقة المقدَّر.</summary>
    public static string TypeLabel(EstimatedDocumentType type) => type switch
    {
        EstimatedDocumentType.SaleReceipt => "وصل بيع",
        EstimatedDocumentType.Invoice => "فاتورة",
        EstimatedDocumentType.AccountStatement => "كشف حساب",
        _ => "غير محدد"
    };

    /// <summary>الاسم المعروض لجزء الوثيقة.</summary>
    public static string PartLabel(EstimatedDocumentPart part) => part switch
    {
        EstimatedDocumentPart.Header => "الرأس",
        EstimatedDocumentPart.Content => "المحتوى",
        EstimatedDocumentPart.Footer => "الذيل",
        _ => "غير مصنَّف"
    };

    /// <summary>الاسم المعروض لفئة الصنف.</summary>
    public static string CategoryLabel(GoodsDecisionCategory category) => category switch
    {
        GoodsDecisionCategory.Registered => "مسجّلة معروفة",
        GoodsDecisionCategory.Unknown => "مجهولة",
        _ => "مخالفة"
    };

    /// <summary>الإجراء المقترح للعامل؛ توصية للعرض فقط، ولا ينفّذها البرنامج.</summary>
    public static string ActionLabel(GoodsDecisionCategory category) => category switch
    {
        GoodsDecisionCategory.Registered => "اعتماد الصنف بعد تأكيده",
        GoodsDecisionCategory.Unknown => "إضافة الصنف أو تجاهله",
        _ => "مراجعة السعر يدويًا"
    };

    /// <summary>الاسم المعروض لمعرفة الاسم بالسجل؛ مشتق من سبب المدمج.</summary>
    public static string KnowledgeLabel(StoreNameKnowledge knowledge) => knowledge switch
    {
        StoreNameKnowledge.Known => "معروف في بيانات المتجر",
        StoreNameKnowledge.Unknown => "غير معروف في بيانات المتجر",
        _ => "يحتاج مراجعة"
    };

    private static IReadOnlyList<DecisionReportPartLine> BuildParts(
        IReadOnlyList<string> lines,
        IReadOnlyList<int> contentIndexes)
    {
        var result = new List<DecisionReportPartLine>(lines.Count);
        if (contentIndexes.Count == 0)
        {
            foreach (var line in lines)
            {
                result.Add(new DecisionReportPartLine(line, EstimatedDocumentPart.Unclassified));
            }

            return result;
        }

        var first = contentIndexes.Min();
        var last = contentIndexes.Max();
        var content = contentIndexes.ToHashSet();

        for (var i = 0; i < lines.Count; i++)
        {
            var part = i < first
                ? EstimatedDocumentPart.Header
                : i > last
                    ? EstimatedDocumentPart.Footer
                    : content.Contains(i)
                        ? EstimatedDocumentPart.Content
                        : EstimatedDocumentPart.Unclassified;

            result.Add(new DecisionReportPartLine(lines[i], part));
        }

        return result;
    }

    private static EstimatedDocumentType DetectDocumentType(
        IReadOnlyList<DecisionReportPartLine> partLines,
        IReadOnlyList<string> fallbackLines)
    {
        var headerText = string.Join(' ', partLines
            .Where(line => line.Part == EstimatedDocumentPart.Header)
            .Select(line => line.Text));

        if (headerText.Length == 0) headerText = string.Join(' ', fallbackLines);
        if (headerText.Length == 0) return EstimatedDocumentType.Unspecified;

        var normalized = TextNormalizer.Normalize(headerText);
        var found = new List<EstimatedDocumentType>();

        if (normalized.Contains("وصل", StringComparison.Ordinal)) found.Add(EstimatedDocumentType.SaleReceipt);
        if (normalized.Contains("فاتور", StringComparison.Ordinal)) found.Add(EstimatedDocumentType.Invoice);
        if (normalized.Contains("كشف", StringComparison.Ordinal)) found.Add(EstimatedDocumentType.AccountStatement);

        return found.Distinct().Count() == 1 ? found[0] : EstimatedDocumentType.Unspecified;
    }

    private static IReadOnlyList<DecisionReportGood> BuildGoods(
        PerceptionResult adjudication,
        IReadOnlyList<DecisionReportPartLine> partLines,
        IReadOnlyDictionary<string, int> lineIndexByText)
    {
        // الأسماء المعروفة تُؤخذ من أحكام المدمج نفسه: سببا القبول واختلاف السعر يقولان صراحةً
        // إن الاسم معروف في بيانات المتجر. لا تُقرأ قاعدة بيانات، ولا يُخترع اسم معروف.
        var knownNames = adjudication.Candidates
            .Where(verdict => verdict.Reason is PerceptionAdjudicator.AcceptedReason
                or PerceptionAdjudicator.ApproximateNameAcceptedReason
                or PerceptionAdjudicator.PriceMismatchReason)
            .Select(verdict => (
                Display: verdict.Candidate.ProductName ?? string.Empty,
                Key: TextNormalizer.Normalize(verdict.Candidate.ProductName ?? string.Empty)))
            .Where(item => item.Key.Length >= 3)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var merged = new List<DecisionReportGood>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var verdict in adjudication.Candidates)
        {
            var rawLine = TextNormalizer.Normalize(verdict.Candidate.RawLine).Trim();
            if (!lineIndexByText.TryGetValue(rawLine, out var lineIndex)) continue;
            if (partLines[lineIndex].Part != EstimatedDocumentPart.Content) continue;

            var name = verdict.Candidate.ProductName ?? MissingValue;
            var price = verdict.Candidate.Price?.ToString("0.##", CultureInfo.InvariantCulture) ?? MissingValue;
            var sourceLine = partLines[lineIndex].Text;
            var key = sourceLine + "\u001f" + name + "\u001f" + price + "\u001f" + verdict.Reason;

            if (positions.TryGetValue(key, out var position))
            {
                merged[position] = merged[position] with { CandidateCount = merged[position].CandidateCount + 1 };
                continue;
            }

            var category = CategoryFor(verdict.Reason);
            var nameKey = TextNormalizer.Normalize(name);
            var nearNames = category == GoodsDecisionCategory.Unknown
                ? knownNames
                    .Where(item => !string.Equals(item.Key, nameKey, StringComparison.Ordinal)
                                   && IsNearName(nameKey, item.Key))
                    .Select(item => item.Display)
                    .Take(3)
                    .ToList()
                : new List<string>();

            positions[key] = merged.Count;
            merged.Add(new DecisionReportGood(
                name,
                price,
                verdict.Reason,
                category,
                KnowledgeFor(verdict.Reason),
                verdict.Accepted,
                sourceLine,
                1,
                nearNames));
        }

        // ترتيب المدمج محفوظ كما صدر؛ الواجهة هي التي تجمّع النتائج حسب السطر عند العرض.
        return merged;
    }

    /// <summary>
    /// تقريب نصي بسيط للمراجعة: احتواء أحد الاسمين في الآخر أو تشابه ثنائيات الحروف.
    /// لا يغيّر حكمًا، بل يُقرّب اسمًا قد يكون مختلفًا قليلاً؛ القرار للعامل.
    /// </summary>
    private static bool IsNearName(string candidate, string known)
    {
        if (candidate.Length < 3 || known.Length < 3) return false;
        if (candidate.Contains(known, StringComparison.Ordinal)
            || known.Contains(candidate, StringComparison.Ordinal)) return true;

        return BigramDice(candidate, known) >= 0.62;
    }

    private static double BigramDice(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return 1.0;
        if (a.Length < 2 || b.Length < 2) return 0.0;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < a.Length - 1; i++)
        {
            var gram = a.Substring(i, 2);
            counts[gram] = counts.GetValueOrDefault(gram) + 1;
        }

        var shared = 0;
        for (var i = 0; i < b.Length - 1; i++)
        {
            var gram = b.Substring(i, 2);
            if (counts.TryGetValue(gram, out var count) && count > 0)
            {
                shared++;
                counts[gram] = count - 1;
            }
        }

        return 2.0 * shared / (a.Length - 1 + b.Length - 1);
    }

    private static GoodsDecisionCategory CategoryFor(string reason) => reason switch
    {
        PerceptionAdjudicator.AcceptedReason => GoodsDecisionCategory.Registered,
        PerceptionAdjudicator.ApproximateNameAcceptedReason => GoodsDecisionCategory.Registered,
        PerceptionAdjudicator.UnknownProductReason => GoodsDecisionCategory.Unknown,
        PerceptionAdjudicator.PriceMismatchReason => GoodsDecisionCategory.Conflicting,
        PerceptionAdjudicator.MissingPriceReason => GoodsDecisionCategory.Conflicting,
        _ => GoodsDecisionCategory.Conflicting
    };

    private static StoreNameKnowledge KnowledgeFor(string reason) => reason switch
    {
        // الأسباب تقول صراحة إن الاسم معروف: قبول حرفي، قبول تقريبي، أو سعر غير مطابق مع بقاء الاسم معروفًا.
        PerceptionAdjudicator.AcceptedReason => StoreNameKnowledge.Known,
        PerceptionAdjudicator.ApproximateNameAcceptedReason => StoreNameKnowledge.Known,
        PerceptionAdjudicator.PriceMismatchReason => StoreNameKnowledge.Known,
        // السبب يقول صراحة إن الاسم غير معروف.
        PerceptionAdjudicator.UnknownProductReason => StoreNameKnowledge.Unknown,
        // لا يوجد سعر مقروء، والسبب لا يحدد معرفة الاسم — لا يُخمَّن.
        _ => StoreNameKnowledge.NotDetermined
    };
}
