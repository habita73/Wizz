namespace Wizz.Application.AI;

using System.Globalization;
using System.Text;
using Wizz.Application.Abstractions;
using Wizz.Domain;

/// <summary>
/// مساعد مدمج سريع لا يحتاج أي نموذج لغوي أو إنترنت.
/// يجيب من بيانات SQLite المحلية مباشرة عبر قواعد نية بسيطة (Intent Rules).
/// يُستخدم كمسار أساسي على الأجهزة الضعيفة، وكبديل تلقائي عند تعذر Ollama.
/// </summary>
public sealed class BuiltInAssistant
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;

    public BuiltInAssistant(ISaleRepository sales, IProductRepository products)
    {
        _sales = sales;
        _products = products;
    }

    public string ProviderName => "المساعد المدمج (بدون نموذج)";

    /// <summary>لقطة سريعة من بيانات المتجر تُستخدم في الإجابات وفي سياق النموذج المحلي.</summary>
    public async Task<StoreSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var todayStart = DateTime.Today.ToUniversalTime();
        var tomorrow = DateTime.Today.AddDays(1).ToUniversalTime();
        var weekStart = DateTime.Today.AddDays(-6).ToUniversalTime();

        var todaySales = await _sales.GetByDateRangeAsync(todayStart, tomorrow, ct);
        var weekSales = await _sales.GetByDateRangeAsync(weekStart, tomorrow, ct);
        var productCount = await _products.CountAsync(ct);
        var debts = await _sales.GetTotalRemainingAsync(ct);

        var activeToday = todaySales.Where(s => s.Status != SaleStatus.Cancelled).ToList();
        var activeWeek = weekSales.Where(s => s.Status != SaleStatus.Cancelled).ToList();

        var products = await _products.GetAllAsync(ct);
        var names = products.ToDictionary(p => p.Id, p => p.Name);

        var top = activeWeek
            .SelectMany(s => s.Items)
            .GroupBy(i => i.ProductId)
            .Select(g => new TopProduct(
                names.TryGetValue(g.Key, out var name) ? name : "منتج محذوف",
                g.Sum(i => i.Quantity),
                g.Sum(i => i.LineTotal)))
            .OrderByDescending(t => t.Revenue)
            .Take(5)
            .ToList();

        return new StoreSnapshot(
            TodaySales: activeToday.Sum(s => s.Total),
            TodayCount: activeToday.Count,
            TodayPaid: activeToday.Sum(s => s.Paid),
            TodayRemaining: activeToday.Sum(s => s.Remaining),
            WeekSales: activeWeek.Sum(s => s.Total),
            WeekCount: activeWeek.Count,
            Debts: debts,
            ProductCount: productCount,
            TopProducts: top);
    }

    /// <summary>يُنتج سياقاً نصياً مضغوطاً يُمرَّر للنموذج المحلي ليجيب بأرقام حقيقية.</summary>
    public static string BuildContextText(StoreSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("بيانات المتجر الحقيقية (مقرأ من قاعدة البيانات المحلية):");
        sb.AppendLine($"- مبيعات اليوم: {s.TodaySales.ToString("N2", CultureInfo.InvariantCulture)} د.ج في {s.TodayCount} عملية.");
        sb.AppendLine($"- مبيعات آخر 7 أيام: {s.WeekSales.ToString("N2", CultureInfo.InvariantCulture)} د.ج في {s.WeekCount} عملية.");
        sb.AppendLine($"- إجمالي الديون المستحقة: {s.Debts.ToString("N2", CultureInfo.InvariantCulture)} د.ج.");
        sb.AppendLine($"- عدد المنتجات المسجلة: {s.ProductCount}.");
        if (s.TopProducts.Count > 0)
        {
            sb.AppendLine("- أفضل المنتجات مبيعاً هذا الأسبوع:");
            foreach (var p in s.TopProducts)
            {
                sb.AppendLine($"  • {p.Name}: {p.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} وحدة، {p.Revenue.ToString("N2", CultureInfo.InvariantCulture)} د.ج");
            }
        }
        return sb.ToString();
    }

    /// <summary>محاولة إجابة السؤال من البيانات المحلية. يعيد null إذا لم تُطابق أي نية معروفة.</summary>
    public async Task<string?> TryAnswerAsync(string question, CancellationToken ct = default)
    {
        var q = Normalize(question);
        if (string.IsNullOrWhiteSpace(q)) return null;

        if (HasAny(q, "مرحبا", "السلام", "أهلا", "اهلا", "صباح", "مساء", "hello", "hi"))
        {
            return "أهلاً بك 👋\nأنا المساعد المدمج في Wizz، أعمل بدون إنترنت وبدون أي نموذج ثقيل.\n\n" +
                   "يمكنني الإجابة فوراً عن:\n" +
                   "• مبيعات اليوم / هذا الأسبوع\n" +
                   "• أفضل المنتجات مبيعاً\n" +
                   "• الديون المستحقة\n" +
                   "• عدد المنتجات\n" +
                   "• طريقة إضافة منتج أو أخذ نسخة احتياطية";
        }

        if (HasAny(q, "مساعدة", "ماذا تستطيع", "ما تستطيع", "قائمة", "help"))
        {
            return "أسئلة يمكنني الإجابة عنها مباشرة من بيانات متجرك:\n" +
                   "• «ما هي مبيعات اليوم؟»\n" +
                   "• «ملخص الأسبوع»\n" +
                   "• «أفضل 5 منتجات مبيعاً»\n" +
                   "• «كم المتبقي من الديون؟»\n" +
                   "• «كم عدد المنتجات؟»\n" +
                   "• «كيف أضيف منتج جديد؟»\n" +
                   "• «كيف أعمل نسخة احتياطية؟»";
        }

        if (HasAny(q, "نسخة", "نسخ احتياطي", "احتياطي", "backup"))
        {
            return "لأخذ نسخة احتياطية:\n" +
                   "1. افتح صفحة «الإعدادات».\n" +
                   "2. انزل إلى قسم «النسخ الاحتياطي».\n" +
                   "3. اضغط «إنشاء نسخة احتياطية».\n\n" +
                   "النسخة تُحفظ محلياً بجانب قاعدة البيانات، وتعمل دون إنترنت.";
        }

        if (q.Contains("منتج") && HasAny(q, "كيف", "أضيف", "اضيف", "إضافة", "اضافة", "جديد"))
        {
            return "لإضافة منتج جديد:\n" +
                   "1. افتح صفحة «المنتجات».\n" +
                   "2. اضغط زر «➕ منتج جديد».\n" +
                   "3. أدخل الاسم، سعر التجزئة، سعر الجملة (إلزامية).\n" +
                   "4. أضف الوحدات إن أردت (قطعة، كجم، علبة...) وحدد الكمية الأساسية لكل وحدة.\n" +
                   "5. اضغط «إنشاء المنتج».\n\n" +
                   "نصيحة: املأ الباركود إن وُجد لتسريع البيع بالمسح.";
        }

        var snapshot = await GetSnapshotAsync(ct);

        if (HasAny(q, "دين", "ديون", "مستحق", "آجل", "اجل", "الباقي"))
        {
            return snapshot.Debts <= 0
                ? "لا توجد ديون مستحقة حالياً ✅\nكل المبيعات الآجلة مسددة."
                : $"إجمالي الديون المستحقة: {snapshot.Debts:N2} د.ج.\n" +
                  $"مبيعات اليوم المتبقي منها: {snapshot.TodayRemaining:N2} د.ج.\n\n" +
                  "يمكنك متابعة التفاصيل من صفحة «المبيعات» ثم فلترة الآجل.";
        }

        if (HasAny(q, "أفضل", "افضل", "الاكثر", "الأكثر", "top") ||
            q.Contains("الأكثر مبيعا", StringComparison.Ordinal) ||
            q.Contains("الاكثر مبيعا", StringComparison.Ordinal))
        {
            if (snapshot.TopProducts.Count == 0)
                return "لا توجد مبيعات مسجلة خلال آخر 7 أيام بعد، لذلك لا يمكن حساب أفضل المنتجات.";

            var sb = new StringBuilder("أفضل المنتجات مبيعاً خلال آخر 7 أيام:\n");
            var rank = 1;
            foreach (var p in snapshot.TopProducts)
            {
                sb.AppendLine($"{rank++}. {p.Name} — {p.Quantity:0.###} وحدة — {p.Revenue:N2} د.ج");
            }
            return sb.ToString().TrimEnd();
        }

        if (HasAny(q, "أسبوع", "اسبوع", "week", "7 أيام", "7 ايام"))
        {
            return $"ملخص آخر 7 أيام:\n" +
                   $"• الإجمالي: {snapshot.WeekSales:N2} د.ج\n" +
                   $"• عدد العمليات: {snapshot.WeekCount}\n" +
                   $"• متوسط العملية: {(snapshot.WeekCount == 0 ? 0 : snapshot.WeekSales / snapshot.WeekCount):N2} د.ج";
        }

        if (HasAny(q, "مبيعات", "بيع", "sale", "sales", "اليوم", "today", "إيراد", "ايراد", "دخل"))
        {
            return $"مبيعات اليوم حتى الآن:\n" +
                   $"• الإجمالي: {snapshot.TodaySales:N2} د.ج\n" +
                   $"• عدد العمليات: {snapshot.TodayCount}\n" +
                   $"• المدفوع: {snapshot.TodayPaid:N2} د.ج\n" +
                   $"• المتبقي (آجل): {snapshot.TodayRemaining:N2} د.ج";
        }

        if (q.Contains("منتج") || HasAny(q, "عدد المنتجات", "المنتجات"))
        {
            return $"عدد المنتجات المسجلة: {snapshot.ProductCount}.\n" +
                   "لعرضها أو تعديلها افتح صفحة «المنتجات».";
        }

        return null;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            sb.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                'ة' => 'ه',
                '\u064B' or '\u064C' or '\u064D' or '\u064E' or '\u064F' or '\u0650' or '\u0651' or '\u0652' => '\0',
                _ => ch
            });
        }
        return sb.Replace("\0", string.Empty).ToString();
    }

    private static bool HasAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
}

/// <summary>لقطة بيانات المتجر المستخدمة في الإجابات المدمجة وسياق النموذج.</summary>
public sealed record StoreSnapshot(
    decimal TodaySales,
    int TodayCount,
    decimal TodayPaid,
    decimal TodayRemaining,
    decimal WeekSales,
    int WeekCount,
    decimal Debts,
    int ProductCount,
    IReadOnlyList<TopProduct> TopProducts);

public sealed record TopProduct(string Name, decimal Quantity, decimal Revenue);
