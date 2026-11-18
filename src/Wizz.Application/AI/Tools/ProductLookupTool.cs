namespace Wizz.Application.AI.Tools;

using System.Globalization;
using System.Text;
using Wizz.Application.AI.Memory;

/// <summary>
/// أداة قراءة آمنة للمنتجات: تجد منتجاً بالاسم أو تعرض سعره بصيغة اللهجة الجزائرية
/// («شحال سعر تفاح»، «دلني على …»). تقرأ من لقطة المتجر نفسها التي يستخدمها المساعد المدمج،
/// فلا تضيف وصولاً جديداً إلى بيانات، ولا تعرض تكلفة الشراء، ولا تكتب في قاعدة البيانات.
/// </summary>
public sealed class ProductLookupTool : IBuiltInTool
{
    private static readonly string[] LookupMarkers =
    {
        "دلني", "وريني", "لقي", "اعطني تفاصيل", "ابحث عن منتج"
    };

    private static readonly string[] PriceWords = { "سعر", "ثمن", "بكم" };

    public string Name => "ProductLookup";

    public string Description => "البحث عن منتج بالاسم وعرض سعره (بما في ذلك مرادفات اللهجة المشتركة).";

    public int Order => 21;

    public async Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
    {
        var q = context.Question;
        var wantsLookup = HasAny(q, LookupMarkers);
        var hasDialectQuantity = HasAny(q, DialectSynonyms.Price);
        var hasPriceWord = HasAny(q, PriceWords) || hasDialectQuantity;
        var wantsPrice = hasDialectQuantity && hasPriceWord;

        if (!wantsLookup && !wantsPrice)
        {
            return null;
        }

        var snapshot = await context.GetSnapshotAsync(ct);
        var tokens = SubjectResolver.ExtractTokens(q);
        var matches = SubjectResolver.MatchProducts(snapshot.Products, tokens);
        if (matches.Count == 0)
        {
            // نترك السؤال لبقية معالجات المدمج بدل إجابة بلا رقم.
            return null;
        }

        return wantsPrice ? FormatPrice(matches) : FormatLookup(matches);
    }

    private static string FormatPrice(IReadOnlyList<ProductInfo> matches)
    {
        if (matches.Count == 1)
        {
            var p = matches[0];
            return $"سعر «{p.Name}»:\n" +
                   $"• التجزئة: {Fmt(p.RetailPrice)} د.ج / {p.Unit}\n" +
                   $"• الجملة: {Fmt(p.WholesalePrice)} د.ج / {p.Unit}" +
                   (p.IsActive ? string.Empty : "\n⚠️ هذا المنتج معطّل حالياً.");
        }

        var sb = new StringBuilder("وجدت عدة منتجات مطابقة:\n");
        foreach (var p in matches)
        {
            sb.AppendLine($"• {p.Name}: تجزئة {Fmt(p.RetailPrice)} د.ج / جملة {Fmt(p.WholesalePrice)} د.ج — {p.Unit}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatLookup(IReadOnlyList<ProductInfo> matches)
    {
        var sb = new StringBuilder($"المنتجات المطابقة ({matches.Count}):\n");
        foreach (var p in matches)
        {
            sb.AppendLine($"• {p.Name} — تجزئة {Fmt(p.RetailPrice)} د.ج / جملة {Fmt(p.WholesalePrice)} د.ج / {p.Unit}" +
                          (p.IsActive ? string.Empty : " (معطّل)"));
        }

        return sb.ToString().TrimEnd();
    }

    private static string Fmt(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static bool HasAny(string text, params string[] keywords)
        => TextNormalizer.HasAny(text, keywords);
}
