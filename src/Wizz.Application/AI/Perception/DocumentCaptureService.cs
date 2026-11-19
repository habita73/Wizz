namespace Wizz.Application.AI.Perception;

using Wizz.Application.Abstractions;
using Wizz.Domain;

/// <summary>طلب تحكيم نص مُعرَّف محلياً، دون التقاط صورة ولا إرسال للخارج.</summary>
/// <param name="RecognizedText">النص المستخرج من أداة محلية أو المُدخل يدوياً.</param>
/// <param name="Source">المسار الذي أنتج النص (مدمج/محلي/سحابي) — لا يمنح ثقة إضافية في الحكم.</param>
public sealed record DocumentCaptureRequest(
    string RecognizedText,
    PerceptionSource Source = PerceptionSource.Embedded);

/// <summary>نتيجة طبقة الإدخال: مرشّحات محكَّمة للعرض فقط، ولا عملية كتابة.</summary>
/// <param name="Adjudication">حكم المدمج على كل مرشّح.</param>
/// <param name="Warning">التنبيه المعتمد بعدم الكتابة التلقائية.</param>
public sealed record DocumentCaptureResult(PerceptionResult Adjudication, string Warning)
{
    /// <summary>يبقى <c>false</c> دائماً في هذه المرحلة.</summary>
    public bool WrittenToDatabase => Adjudication.WrittenToDatabase;
}

/// <summary>
/// طبقة إدخال نص محلي فوق نواة التعرّف: تحوّل النص المُعرَّف إلى مرشّحات، ثم تعرض حكم المدمج
/// مقابل أسعار المنتجات النشطة في المتجر. لا تكتب في قاعدة البيانات، لا ترسل أي شيء للخارج،
/// ولا تمرّ عبر موجه النماذج، لذلك لا تؤثر على قرارات التوجيه أو أسئلة الأرقام.
/// </summary>
public sealed class DocumentCaptureService
{
    /// <summary>أقصى طول نص مُدخل؛ ما زاد يُقتطع لحماية الذاكرة والواجهة.</summary>
    public const int MaxTextLength = 4000;

    private readonly IProductRepository _products;

    public DocumentCaptureService(IProductRepository products)
    {
        _products = products;
    }

    /// <summary>يحكّم النص المُدخل ويُعيد النتيجة للعرض فقط، دون أي تغيير في البيانات.</summary>
    public async Task<DocumentCaptureResult> CaptureAsync(
        DocumentCaptureRequest request,
        CancellationToken ct = default)
    {
        var text = request.RecognizedText?.Trim() ?? string.Empty;
        if (text.Length > MaxTextLength)
        {
            text = text[..MaxTextLength];
        }

        var groups = PerceptionCandidateExtractor.ExtractGroups(text, request.Source);
        var known = await BuildKnownProductsAsync(ct);
        var (primaries, possibleReadings) =
            PerceptionCandidateExtractor.SelectPrimaryReadings(groups, known);
        var adjudication = PerceptionAdjudicator.Adjudicate(primaries, known)
            with { PossibleReadings = possibleReadings };

        return new DocumentCaptureResult(adjudication, PerceptionAdjudicator.NoWriteReason);
    }

    /// <summary>
    /// يبني خريطة «الاسم المطبَّع → الأسعار المعتمدة» من المنتجات النشطة فقط،
    /// باستعلام قراءة واحد عبر مستودع المنتجات. لا SQL حر، ولا كتابة.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<decimal>>> BuildKnownProductsAsync(CancellationToken ct)
    {
        var products = await _products.GetAllAsync(ct);
        var map = new Dictionary<string, List<decimal>>(StringComparer.Ordinal);

        foreach (var product in products)
        {
            if (!product.IsActive || string.IsNullOrWhiteSpace(product.Name)) continue;

            var key = TextNormalizer.Normalize(product.Name);
            if (key.Length == 0) continue;

            if (!map.TryGetValue(key, out var prices))
            {
                prices = new List<decimal>();
                map[key] = prices;
            }

            foreach (var price in new[] { product.RetailPrice, product.WholesalePrice })
            {
                if (price > 0m && !prices.Contains(price))
                {
                    prices.Add(price);
                }
            }
        }

        return map.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<decimal>)kvp.Value,
            StringComparer.Ordinal);
    }
}
