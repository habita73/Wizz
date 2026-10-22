namespace Wizz.Application.AI;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Wizz.Application.Abstractions;
using Wizz.Application.AI.Artifacts;
using Wizz.Application.AI.Tools;
using Wizz.Domain;

/// <summary>
/// مساعد مدمج سريع لا يحتاج أي نموذج لغوي أو إنترنت.
/// يفهم نية السؤال (قائمة/مجموع/أكبر/أقدم/سعر/فترة...) ويجيب من SQLite المحلية.
/// يُستخدم كمسار أساسي على الأجهزة الضعيفة، وكبديل تلقائي عند تعذر Ollama.
/// </summary>
public sealed class BuiltInAssistant
{
    /// <summary>أسئلة تحتاج تحليلاً/رأياً — تُوجَّه للنموذج المحلي أولاً إن كان متاحاً.</summary>
    private static readonly string[] AnalyticalMarkers =
    {
        "لماذا", "حلل", "تحليل", "اقترح", "ماذا لو", "توقع", "توقعات",
        "قارن", "مقارنه", "اشرح", "راي", "نصيحه", "كيف احسن", "كيف اطور",
        "خطه", "استراتيجيه", "سبب", "تنبؤ"
    };

    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IStoreRepository _store;
    private readonly BuiltInToolRegistry _tools;

    public BuiltInAssistant(ISaleRepository sales, IProductRepository products, IStoreRepository store)
        : this(sales, products, store, BuiltInToolRegistry.CreateDefault())
    {
    }

    public BuiltInAssistant(
        ISaleRepository sales,
        IProductRepository products,
        IStoreRepository store,
        BuiltInToolRegistry tools)
    {
        _sales = sales;
        _products = products;
        _store = store;
        _tools = tools;
    }

    public string ProviderName => "المساعد المدمج (بدون نموذج)";

    /// <summary>هل السؤال تحليلي؟ (يُفضَّل معه النموذج المحلي إن وُجد)</summary>
    public static bool IsAnalytical(string question)
        => HasAny(Normalize(question), AnalyticalMarkers);

    /// <summary>لقطة سريعة من بيانات المتجر تُستخدم في الإجابات وفي سياق النموذج المحلي.</summary>
    public async Task<StoreSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var all = await _sales.GetByDateRangeAsync(DateTime.UnixEpoch, DateTime.UtcNow.AddDays(1), ct);
        var active = all.Where(s => s.Status != SaleStatus.Cancelled).ToList();

        var today = DateTime.Today;
        var todayStart = today.ToUniversalTime();
        var tomorrow = today.AddDays(1).ToUniversalTime();
        var yesterdayStart = today.AddDays(-1).ToUniversalTime();
        var weekStart = today.AddDays(-6).ToUniversalTime();
        var monthStart = new DateTime(today.Year, today.Month, 1).ToUniversalTime();

        var todaySales = active.Where(s => s.SaleDate >= todayStart && s.SaleDate < tomorrow).ToList();
        var yesterdaySales = active.Where(s => s.SaleDate >= yesterdayStart && s.SaleDate < todayStart).ToList();
        var weekSales = active.Where(s => s.SaleDate >= weekStart).ToList();
        var monthSales = active.Where(s => s.SaleDate >= monthStart).ToList();

        var products = await _products.GetAllAsync(ct);
        var names = products.ToDictionary(p => p.Id, p => p.Name);

        // أفضل المنتجات: آخر 7 أيام، وإن لم توجد مبيعات نستخدم كل السجل
        var topSource = weekSales.Count > 0 ? weekSales : active;
        var top = topSource
            .SelectMany(s => s.Items)
            .GroupBy(i => i.ProductId)
            .Select(g => new TopProduct(
                names.TryGetValue(g.Key, out var name) ? name : "منتج محذوف",
                g.Sum(i => i.Quantity),
                g.Sum(i => i.LineTotal)))
            .OrderByDescending(t => t.Revenue)
            .Take(10)
            .ToList();

        var debts = active
            .Where(s => s.Remaining > 0)
            .OrderByDescending(s => s.Remaining)
            .Select(s => new DebtItem(
                s.Id,
                s.Id.ToString("N")[..8].ToUpperInvariant(),
                s.SaleDate,
                s.Remaining,
                s.DueDate,
                s.DueDate.HasValue && s.DueDate.Value.Date < DateTime.UtcNow.Date,
                s.DueDate.HasValue ? (int)(DateTime.UtcNow.Date - s.DueDate.Value.Date).TotalDays : 0))
            .ToList();

        var store = await _store.GetAsync(ct);

        return new StoreSnapshot(
            TodaySales: todaySales.Sum(s => s.Total),
            TodayCount: todaySales.Count,
            TodayPaid: todaySales.Sum(s => s.Paid),
            TodayRemaining: todaySales.Sum(s => s.Remaining),
            YesterdaySales: yesterdaySales.Sum(s => s.Total),
            YesterdayCount: yesterdaySales.Count,
            YesterdayPaid: yesterdaySales.Sum(s => s.Paid),
            YesterdayRemaining: yesterdaySales.Sum(s => s.Remaining),
            WeekSales: weekSales.Sum(s => s.Total),
            WeekCount: weekSales.Count,
            MonthSales: monthSales.Sum(s => s.Total),
            MonthCount: monthSales.Count,
            LifetimeSales: active.Sum(s => s.Total),
            LifetimeCount: active.Count,
            DebtsTotal: debts.Sum(d => d.Remaining),
            Debts: debts,
            ProductCount: products.Count,
            ActiveProductCount: products.Count(p => p.IsActive),
            StoreName: store?.Name ?? "غير معد",
            StoreAddress: store?.Address,
            StorePhone: store?.Phone,
            TopProducts: top,
            Products: products
                .Select(p => new ProductInfo(
                    p.Name,
                    p.Barcode,
                    p.RetailPrice,
                    p.WholesalePrice,
                    p.GetBaseUnit()?.Symbol ?? "قطعة",
                    p.IsActive))
                .ToList());
    }

    /// <summary>سياق نصي مضغوط يُمرَّر للنموذج المحلي ليجيب بأرقام حقيقية.</summary>
    public static string BuildContextText(StoreSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("بيانات المتجر الحقيقية (مقروءة من قاعدة البيانات المحلية):");
        sb.AppendLine($"- المتجر: {s.StoreName}، عدد المنتجات: {s.ProductCount} (نشط: {s.ActiveProductCount}).");
        sb.AppendLine($"- اليوم: مبيعات {Fmt(s.TodaySales)} د.ج في {s.TodayCount} عملية، مدفوع {Fmt(s.TodayPaid)}، متبقٍ {Fmt(s.TodayRemaining)}.");
        sb.AppendLine($"- أمس: مبيعات {Fmt(s.YesterdaySales)} د.ج في {s.YesterdayCount} عملية.");
        sb.AppendLine($"- آخر 7 أيام: {Fmt(s.WeekSales)} د.ج في {s.WeekCount} عملية.");
        sb.AppendLine($"- هذا الشهر: {Fmt(s.MonthSales)} د.ج في {s.MonthCount} عملية.");
        sb.AppendLine($"- الإجمالي الكلي: {Fmt(s.LifetimeSales)} د.ج في {s.LifetimeCount} عملية.");
        sb.AppendLine($"- الديون المستحقة: {Fmt(s.DebtsTotal)} د.ج موزعة على {s.Debts.Count} عملية آجلة.");
        foreach (var d in s.Debts.Take(10))
        {
            sb.AppendLine($"  • فاتورة #{d.SaleNumber} بتاريخ {d.SaleDate.ToLocalTime():yyyy-MM-dd}: متبقٍ {Fmt(d.Remaining)} د.ج" +
                          (d.DueDate.HasValue ? $"، استحقاق {d.DueDate.Value.ToLocalTime():yyyy-MM-dd}" : string.Empty) +
                          (d.IsOverdue ? $" (متأخر {d.DaysOverdue} يوم)" : string.Empty));
        }

        if (s.TopProducts.Count > 0)
        {
            sb.AppendLine("- الأكثر مبيعاً:");
            foreach (var p in s.TopProducts.Take(5))
            {
                sb.AppendLine($"  • {p.Name}: {p.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} وحدة، {Fmt(p.Revenue)} د.ج");
            }
        }

        if (s.Products.Count > 0)
        {
            sb.AppendLine("- المنتجات وأسعارها (تجزئة/جملة):");
            foreach (var p in s.Products.Take(25))
            {
                sb.AppendLine($"  • {p.Name} [{p.Unit}]: {Fmt(p.RetailPrice)} / {Fmt(p.WholesalePrice)} د.ج" +
                              (p.IsActive ? string.Empty : " (معطّل)") +
                              (string.IsNullOrWhiteSpace(p.Barcode) ? string.Empty : $" — باركود {p.Barcode}"));
            }
            if (s.Products.Count > 25) sb.AppendLine($"  … و{s.Products.Count - 25} منتجاً آخر.");
        }

        return sb.ToString();
    }

    /// <summary>محاولة إجابة السؤال من البيانات المحلية. يعيد null إذا لم تُطابق أي نية معروفة.</summary>
    public async Task<string?> TryAnswerAsync(string question, CancellationToken ct = default)
    {
        var q = Normalize(question);
        if (q.Length == 0) return null;

        if (HasAny(q, "مرحبا", "السلام", "اهلا", "صباح", "مساء", "hello", "hi"))
        {
            return "أهلاً بك 👋\nأنا المساعد المدمج في Wizz، أعمل بدون إنترنت.\n" +
                   "اسألني مثلاً: «قائمة الديون»، «مجموع ديون اليوم»، «أكبر دين»، «مبيعات أمس»، " +
                   "«أفضل 3 منتجات»، «سعر تفاح»، أو «كيف أعمل نسخة احتياطية؟».";
        }

        if (HasAny(q, "مساعده", "ماذا تستطيع", "ما تستطيع", "help", "قائمه الاوامر"))
        {
            return "أفهم أسئلة مثل:\n" +
                   "• المبيعات: اليوم / أمس / هذا الأسبوع / هذا الشهر / الإجمالي.\n" +
                   "• الديون: قائمة الديون / مجموع الديون / أكبر دين / أقدم دين / عدد الديون.\n" +
                   "• المنتجات: عدد المنتجات / قائمة المنتجات / سعر منتج / المنتجات المعطلة.\n" +
                   "• النظام: الوقت والتاريخ / مساحة القرص / مواصفات الجهاز / قوالب الطباعة.\n" +
                   "• مساعدة: إضافة منتج، تصحيح أو إلغاء بيع، طباعة إيصال، نسخة احتياطية، معلومات المتجر.";
        }

        // ---- أدوات النظام (وقت/أقراص/طباعة/مواصفات) ----
        var toolContext = new BuiltInToolContext
        {
            Question = q,
            RawQuestion = question,
            SnapshotFactory = GetSnapshotAsync
        };
        var toolAnswer = await _tools.TryAnswerAsync(toolContext, ct);
        if (toolAnswer is not null)
            return toolAnswer;

        // ---- أسئلة إجرائية (كيف أفعل...) ----
        if (HasAny(q, "نسخه", "نسخ احتياطي", "احتياطي", "backup"))
        {
            return "لأخذ نسخة احتياطية:\n1) افتح «الإعدادات».\n2) انزل إلى قسم «النسخ الاحتياطي».\n3) اضغط «إنشاء نسخة احتياطية».\n\nالنسخة تُحفظ محلياً وتعمل دون إنترنت.";
        }

        if (q.Contains("منتج") && HasAny(q, "اضيف", "اضافه", "انشئ", "اسجل", "كيف"))
        {
            return "لإضافة منتج:\n1) صفحة «المنتجات» ← «➕ منتج جديد».\n2) الاسم وسعر التجزئة وسعر الجملة إلزامية.\n3) أضف الوحدات (قطعة/كجم/علبة) مع الكمية الأساسية.\n4) «إنشاء المنتج».\n\nنصيحة: املأ الباركود لتسريع البيع بالمسح.";
        }

        if (HasAny(q, "تصحيح", "اصحح", "الغاء", "الغي") && HasAny(q, "بيع", "فاتوره", "عمليه"))
        {
            return "لتصحيح أو إلغاء بيع:\n1) افتح «سجل المبيعات».\n2) اختر العملية ← «تصحيح» أو «إلغاء».\n3) اكتب السبب وأكّد.\n\nيُحفظ السجل الأصلي كأثر تدقيقي، ولا يُحذف.";
        }

        if (HasAny(q, "اطبع", "طباعه", "ايصال") && !HasAny(q, "دين", "ديون", "مستحق", "متبقي", "الباقي"))
        {
            return "لطباعة إيصال:\n• بعد إتمام البيع مباشرة: زر «🖨️ طباعة آخر إيصال».\n• لبيع سابق: «سجل المبيعات» ← اختر العملية ← «إيصال».\n\nحالياً الطابعة تجريبية (Mock) وتُظهر الإيصال كنص.";
        }

        var s = await GetSnapshotAsync(ct);

        // ---- الديون ----
        if (HasAny(q, "دين", "ديون", "مستحق", "اجل", "الاجل", "الباقي", "المتبقي"))
        {
            if (HasAny(q, "قائمه", "قايمه", "اعرض", "ارني", "اسرد", "كل الديون", "الديون كلها", "فواتير", "تفاصيل"))
            {
                return FormatDebtList(s);
            }

            if (HasAny(q, "اكبر", "اعلي", "الاكثر"))
            {
                return FormatLargestDebt(s);
            }

            if (HasAny(q, "اقدم", "اقرب", "متاخر", "فائت", "انتهي", "تجاوز", "استحق"))
            {
                return FormatOldestDebt(s);
            }

            if (HasAny(q, "كم عدد", "عدد الديون", "عدد العمليات", "عدد الفواتير", "كم فاتوره"))
            {
                return FormatDebtCount(s);
            }

            return FormatDebtTotal(s);
        }

        // ---- أفضل المنتجات ----
        if (HasAny(q, "افضل", "الاكثر مبيعا", "الاكثر طلبا", "top") ||
            (q.Contains("اكثر") && q.Contains("مبيع")))
        {
            var limit = ParseCount(q, 5, 1, 20);
            if (s.TopProducts.Count == 0)
            {
                return "لا توجد مبيعات مسجلة بعد، لذلك لا يمكن حساب الأكثر مبيعاً.";
            }

            var shown = Math.Min(limit, s.TopProducts.Count);
            var scope = shown == 1 ? "منتج واحد" : $"{shown} منتجات";
            var sb = new StringBuilder($"أفضل المنتجات مبيعاً ({scope}):\n");
            var rank = 1;
            foreach (var p in s.TopProducts.Take(limit))
            {
                sb.AppendLine($"{rank++}. {p.Name} — {p.Quantity:0.###} وحدة — {Fmt(p.Revenue)} د.ج");
            }

            // مخرج قابل للتصدير: جدول تفاعلي (تنزيل CSV/JSON، طباعة).
            sb.AppendLine();
            sb.Append(ArtifactMarkers.Table(
                $"أفضل المنتجات مبيعاً ({scope})",
                new[] { "#", "المنتج", "الكمية", "الإيراد (د.ج)" },
                s.TopProducts.Take(limit)
                    .Select((p, i) => (IReadOnlyList<string>)new[]
                    {
                        (i + 1).ToString(CultureInfo.InvariantCulture),
                        p.Name,
                        p.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                        Fmt(p.Revenue)
                    })
                    .ToList()));

            return sb.ToString().TrimEnd();
        }

        // ---- المبيعات حسب الفترة ----
        if (HasAny(q, "مبيعات", "بيع", "ايراد", "دخل", "مدخول", "sale", "sales"))
        {
            if (HasAny(q, "امس"))
                return FormatPeriod("أمس", s.YesterdaySales, s.YesterdayCount, s.YesterdayPaid, s.YesterdayRemaining);

            if (HasAny(q, "اسبوع", "7 ايام"))
                return FormatPeriod("آخر 7 أيام", s.WeekSales, s.WeekCount, s.WeekSales - s.TodayRemaining, null);

            if (HasAny(q, "شهر"))
                return FormatPeriod("هذا الشهر", s.MonthSales, s.MonthCount, null, null);

            if (HasAny(q, "كلي", "اجمالي", "كل الفترات", "من البدايه"))
                return FormatPeriod("كل الفترات", s.LifetimeSales, s.LifetimeCount, null, null);

            return $"مبيعات اليوم حتى الآن:\n" +
                   $"• الإجمالي: {Fmt(s.TodaySales)} د.ج\n" +
                   $"• عدد العمليات: {s.TodayCount}\n" +
                   $"• المدفوع: {Fmt(s.TodayPaid)} د.ج\n" +
                   $"• المتبقي (آجل): {Fmt(s.TodayRemaining)} د.ج\n" +
                   $"• آخر 7 أيام: {Fmt(s.WeekSales)} د.ج في {s.WeekCount} عملية";
        }

        // ---- المنتجات ----
        if (HasAny(q, "سعر", "بكم", "ثمن", "كم سعر"))
        {
            return FormatPrice(s, q);
        }

        if (q.Contains("منتج") || HasAny(q, "المنتجات"))
        {
            if (HasAny(q, "غير نشط", "معطل", "موقوف", "متوقف"))
            {
                var inactive = s.Products.Where(p => !p.IsActive).Select(p => p.Name).ToList();
                return inactive.Count == 0
                    ? "كل المنتجات نشطة، لا يوجد منتج معطّل."
                    : $"المنتجات المعطلة ({inactive.Count}):\n" + string.Join("\n", inactive.Select(n => $"• {n}"));
            }

            if (HasAny(q, "قائمه", "قايمه", "اعرض", "ارني", "اسرد", "كل المنتجات"))
            {
                var list = s.Products.Take(15).ToList();
                if (list.Count == 0) return "لا توجد منتجات مسجلة بعد.";
                var sb = new StringBuilder($"المنتجات ({s.ProductCount}، النشط منها {s.ActiveProductCount}):\n");
                foreach (var p in list)
                {
                    sb.AppendLine($"• {p.Name} — {Fmt(p.RetailPrice)} د.ج / {p.Unit}{(p.IsActive ? string.Empty : " (معطّل)")}");
                }
                if (s.Products.Count > list.Count) sb.AppendLine($"… و{s.Products.Count - list.Count} منتجاً آخر في صفحة المنتجات.");
                return sb.ToString().TrimEnd();
            }

            return $"عدد المنتجات: {s.ProductCount} (نشط: {s.ActiveProductCount}، معطّل: {s.ProductCount - s.ActiveProductCount}).\n" +
                   "لعرضها أو تعديل أسعارها افتح صفحة «المنتجات».";
        }

        // ---- معلومات المتجر ----
        if (HasAny(q, "اسم المتجر", "المتجر", "عنوان", "هاتف", "رقم الهاتف"))
        {
            return $"بيانات المتجر:\n• الاسم: {s.StoreName}\n• العنوان: {s.StoreAddress ?? "غير محدد"}\n• الهاتف: {s.StorePhone ?? "غير محدد"}";
        }

        return null;
    }

    private static string FormatDebtTotal(StoreSnapshot s)
    {
        if (s.Debts.Count == 0)
            return "لا توجد ديون مستحقة حالياً ✅\nكل المبيعات الآجلة مسددة.";

        var overdue = s.Debts.Count(d => d.IsOverdue);
        return $"مجموع الديون المستحقة: {Fmt(s.DebtsTotal)} د.ج\n" +
               $"• عدد العمليات الآجلة غير المسددة: {s.Debts.Count}\n" +
               $"• أكبرها: {Fmt(s.Debts[0].Remaining)} د.ج (فاتورة #{s.Debts[0].SaleNumber})\n" +
               $"• المتأخرة عن تاريخ الاستحقاق: {overdue}\n\n" +
               "اكتب «قائمة الديون» لعرض التفاصيل، أو «أكبر دين» لأكبر عملية.";
    }

    private static string FormatDebtList(StoreSnapshot s)
    {
        if (s.Debts.Count == 0)
            return "لا توجد ديون مستحقة حالياً ✅";

        var sb = new StringBuilder($"قائمة الديون ({s.Debts.Count} عملية، الإجمالي {Fmt(s.DebtsTotal)} د.ج):\n");
        var i = 1;
        foreach (var d in s.Debts.Take(20))
        {
            var due = d.DueDate.HasValue ? $" — استحقاق {d.DueDate.Value.ToLocalTime():yyyy-MM-dd}" : string.Empty;
            var late = d.IsOverdue ? $" ⏰ متأخر {d.DaysOverdue} يوم" : string.Empty;
            sb.AppendLine($"{i++}. فاتورة #{d.SaleNumber} — {d.SaleDate.ToLocalTime():yyyy-MM-dd} — متبقٍ {Fmt(d.Remaining)} د.ج{due}{late}");
        }
        if (s.Debts.Count > 20) sb.AppendLine($"… و{s.Debts.Count - 20} عملية أخرى.");
        return sb.ToString().TrimEnd();
    }

    private static string FormatLargestDebt(StoreSnapshot s)
    {
        if (s.Debts.Count == 0) return "لا توجد ديون مستحقة حالياً ✅";

        var d = s.Debts[0];
        var due = d.DueDate.HasValue ? $"\n• الاستحقاق: {d.DueDate.Value.ToLocalTime():yyyy-MM-dd}" : string.Empty;
        var late = d.IsOverdue ? $"\n⏰ متأخر بـ {d.DaysOverdue} يوم" : string.Empty;
        return $"أكبر دين: {Fmt(d.Remaining)} د.ج\n" +
               $"• الفاتورة: #{d.SaleNumber}\n" +
               $"• التاريخ: {d.SaleDate.ToLocalTime():yyyy-MM-dd}{due}{late}\n" +
               $"• من أصل {s.Debts.Count} عملية آجلة غير مسددة.";
    }

    private static string FormatOldestDebt(StoreSnapshot s)
    {
        if (s.Debts.Count == 0) return "لا توجد ديون مستحقة حالياً ✅";

        var overdue = s.Debts.Where(d => d.IsOverdue).OrderByDescending(d => d.DaysOverdue).FirstOrDefault();
        if (overdue is not null)
        {
            return $"أقدم دين متأخر: {Fmt(overdue.Remaining)} د.ج\n" +
                   $"• الفاتورة: #{overdue.SaleNumber} بتاريخ {overdue.SaleDate.ToLocalTime():yyyy-MM-dd}\n" +
                   $"• الاستحقاق: {overdue.DueDate!.Value.ToLocalTime():yyyy-MM-dd} (متأخر {overdue.DaysOverdue} يوماً)\n" +
                   $"• الديون المتأخرة: {s.Debts.Count(d => d.IsOverdue)} من {s.Debts.Count}.";
        }

        var oldest = s.Debts.OrderBy(d => d.SaleDate).First();
        var due = oldest.DueDate.HasValue ? $"\n• الاستحقاق: {oldest.DueDate.Value.ToLocalTime():yyyy-MM-dd}" : string.Empty;
        return $"لا توجد ديون متأخرة ✅\nأقدم دين غير مسدد: {Fmt(oldest.Remaining)} د.ج — فاتورة #{oldest.SaleNumber} بتاريخ {oldest.SaleDate.ToLocalTime():yyyy-MM-dd}.{due}";
    }

    private static string FormatDebtCount(StoreSnapshot s)
        => s.Debts.Count == 0
            ? "عدد الديون المستحقة: 0 — لا توجد مبيعات آجلة غير مسددة."
            : $"عدد الديون المستحقة: {s.Debts.Count} عملية، بإجمالي {Fmt(s.DebtsTotal)} د.ج.\n" +
              $"المتأخرة منها: {s.Debts.Count(d => d.IsOverdue)} — ومتوسط الدين: {Fmt(s.DebtsTotal / s.Debts.Count)} د.ج.";

    private static string FormatPeriod(string label, decimal total, int count, decimal? paid, decimal? remaining)
    {
        var sb = new StringBuilder($"مبيعات {label}:\n• الإجمالي: {Fmt(total)} د.ج\n• عدد العمليات: {count}");
        if (count > 0) sb.Append($"\n• متوسط العملية: {Fmt(total / count)} د.ج");
        if (paid.HasValue) sb.Append($"\n• المدفوع: {Fmt(paid.Value)} د.ج");
        if (remaining.HasValue) sb.Append($"\n• المتبقي (آجل): {Fmt(remaining.Value)} د.ج");
        return sb.ToString();
    }

    private static string FormatPrice(StoreSnapshot s, string q)
    {
        var tokens = q
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !HasAny(t, "سعر", "ثمن", "بكم", "منتج", "كام", "كم", "هو", "هي", "المنتج"))
            .ToList();

        if (tokens.Count == 0)
            return "اكتب اسم المنتج بعد كلمة «سعر»، مثال: «كم سعر تفاح».";

        var matches = s.Products
            .Where(p => tokens.Any(t => Normalize(p.Name).Contains(t, StringComparison.Ordinal)
                                        || (p.Barcode?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)))
            .Take(5)
            .ToList();

        if (matches.Count == 0)
            return "لم أجد منتجاً بهذا الاسم. تحقق من الاسم أو ابحث في صفحة «المنتجات».";

        if (matches.Count == 1)
        {
            var p = matches[0];
            var status = p.IsActive ? string.Empty : "\n⚠️ هذا المنتج معطّل حالياً.";
            return $"سعر «{p.Name}»:\n• التجزئة: {Fmt(p.RetailPrice)} د.ج / {p.Unit}\n• الجملة: {Fmt(p.WholesalePrice)} د.ج / {p.Unit}{status}";
        }

        var sb = new StringBuilder("وجدت عدة منتجات مطابقة:\n");
        foreach (var p in matches)
        {
            sb.AppendLine($"• {p.Name}: تجزئة {Fmt(p.RetailPrice)} د.ج / جملة {Fmt(p.WholesalePrice)} د.ج — {p.Unit}");
        }
        return sb.ToString().TrimEnd();
    }

    private static int ParseCount(string q, int fallback, int min, int max)
    {
        var m = Regex.Match(q, @"\d{1,2}");
        if (!m.Success) return fallback;
        var value = int.Parse(m.Value, CultureInfo.InvariantCulture);
        return Math.Clamp(value, min, max);
    }

    private static string Fmt(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.Trim().ToLowerInvariant())
        {
            // توحيد الأرقام العربية والفارسية
            if (raw is >= '\u0660' and <= '\u0669') { sb.Append((char)('0' + (raw - '\u0660'))); continue; }
            if (raw is >= '\u06F0' and <= '\u06F9') { sb.Append((char)('0' + (raw - '\u06F0'))); continue; }

            sb.Append(raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                'ة' => 'ه',
                '\u064B' or '\u064C' or '\u064D' or '\u064E' or '\u064F' or '\u0650' or '\u0651' or '\u0652' or '\u0640' => '\0',
                _ => raw
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
    decimal YesterdaySales,
    int YesterdayCount,
    decimal YesterdayPaid,
    decimal YesterdayRemaining,
    decimal WeekSales,
    int WeekCount,
    decimal MonthSales,
    int MonthCount,
    decimal LifetimeSales,
    int LifetimeCount,
    decimal DebtsTotal,
    IReadOnlyList<DebtItem> Debts,
    int ProductCount,
    int ActiveProductCount,
    string StoreName,
    string? StoreAddress,
    string? StorePhone,
    IReadOnlyList<TopProduct> TopProducts,
    IReadOnlyList<ProductInfo> Products);

public sealed record TopProduct(string Name, decimal Quantity, decimal Revenue);

public sealed record DebtItem(
    Guid SaleId,
    string SaleNumber,
    DateTime SaleDate,
    decimal Remaining,
    DateTime? DueDate,
    bool IsOverdue,
    int DaysOverdue);

public sealed record ProductInfo(
    string Name,
    string? Barcode,
    decimal RetailPrice,
    decimal WholesalePrice,
    string Unit,
    bool IsActive);
