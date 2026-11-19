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

/// <summary>سطر من النص المقروء مع جزئه المقدَّر.</summary>
/// <param name="Text">النص كما قرأه التطبيق.</param>
/// <param name="Part">الجزء المقدَّر حسب الموضع.</param>
public sealed record DecisionReportPartLine(string Text, EstimatedDocumentPart Part);

/// <summary>صنف في تقرير القرار؛ كل حقل منقول من نتيجة المحكّم.</summary>
/// <param name="Name">الاسم كما قرأه التطبيق.</param>
/// <param name="Price">السعر كما قرأه التطبيق أو «—».</param>
/// <param name="Verdict">سبب المحكّم كما صدر، دون تعديل.</param>
/// <param name="Category">الفئة المشتقّة من السبب.</param>
/// <param name="Accepted">حكم المحكّم.</param>
public sealed record DecisionReportGood(
    string Name,
    string Price,
    string Verdict,
    GoodsDecisionCategory Category,
    bool Accepted);

/// <summary>تقرير قرار للعرض فقط: ناقل لأحكام المحكّم، لا حاكم جديد.</summary>
/// <param name="DocumentType">نوع الوثيقة المقدَّر.</param>
/// <param name="PartLines">أسطر النص مع أجزائها المقدَّرة.</param>
/// <param name="Goods">أصناف التقرير بترتيب أحكام المدمج.</param>
/// <param name="RegisteredCount">عدد أصناف «مسجّلة معروفة».</param>
/// <param name="UnknownCount">عدد أصناف «مجهولة».</param>
/// <param name="ConflictingCount">عدد أصناف «مخالفة».</param>
/// <param name="AcceptedCount">عدد المقبول كما صدر عن المحكّم.</param>
/// <param name="RejectedCount">عدد المرفوض كما صدر عن المحكّم.</param>
/// <param name="WrittenToDatabase">يبقى <c>false</c> دائمًا.</param>
public sealed record DecisionReport(
    EstimatedDocumentType DocumentType,
    IReadOnlyList<DecisionReportPartLine> PartLines,
    IReadOnlyList<DecisionReportGood> Goods,
    int RegisteredCount,
    int UnknownCount,
    int ConflictingCount,
    int AcceptedCount,
    int RejectedCount,
    bool WrittenToDatabase);

/// <summary>
/// يبني تقرير القرار من نتيجة المحكّم المدمج ومن النص المقروء فقط. لا يقرأ قاعدة بيانات،
/// لا يرسل، لا يحسب أسعارًا، ولا يخترع حكمًا؛ كل ما يفعله هو نقل الأحكام وتجميعها للعرض.
/// </summary>
public static class DecisionReportBuilder
{
    /// <summary>الوسم الذي يُلصق بنوع الوثيقة وتقسيم أجزائها.</summary>
    public const string EstimatedLabel = "تقديري";

    /// <summary>يُستخدم حين لا يتوفر اسم أو سعر مقروء.</summary>
    public const string MissingValue = "—";

    // قرينة تقريبية لصف صنف: رقم عشري مثل 12.5 أو 180.00. الرأس والتذييل لا يحويان عادةً رقمًا عشريًا.
    private static readonly Regex DecimalToken = new(@"\d+[.,]\d+", RegexOptions.Compiled);

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
        // والتذييل أيضًا. القرينة التقديرية هي رقم عشري في السطر، ثم يُحدد الرأس والذيل بالموضع.
        var contentIndexes = lines
            .Select((line, index) => (line, index))
            .Where(item => DecimalToken.IsMatch(item.line))
            .Select(item => item.index)
            .ToList();

        var partLines = BuildParts(lines, contentIndexes);
        var documentType = DetectDocumentType(partLines, lines);
        var goods = BuildGoods(adjudication);

        return new DecisionReport(
            documentType,
            partLines,
            goods,
            goods.Count(item => item.Category == GoodsDecisionCategory.Registered),
            goods.Count(item => item.Category == GoodsDecisionCategory.Unknown),
            goods.Count(item => item.Category == GoodsDecisionCategory.Conflicting),
            adjudication.AcceptedCount,
            adjudication.RejectedCount,
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

    private static IReadOnlyList<DecisionReportGood> BuildGoods(PerceptionResult adjudication)
    {
        var goods = new List<DecisionReportGood>(adjudication.Candidates.Count);
        foreach (var verdict in adjudication.Candidates)
        {
            var category = verdict.Reason switch
            {
                PerceptionAdjudicator.AcceptedReason => GoodsDecisionCategory.Registered,
                PerceptionAdjudicator.UnknownProductReason => GoodsDecisionCategory.Unknown,
                PerceptionAdjudicator.PriceMismatchReason => GoodsDecisionCategory.Conflicting,
                PerceptionAdjudicator.MissingPriceReason => GoodsDecisionCategory.Conflicting,
                _ => GoodsDecisionCategory.Conflicting
            };

            goods.Add(new DecisionReportGood(
                verdict.Candidate.ProductName ?? MissingValue,
                verdict.Candidate.Price?.ToString("0.##", CultureInfo.InvariantCulture) ?? MissingValue,
                verdict.Reason,
                category,
                verdict.Accepted));
        }

        return goods;
    }
}
