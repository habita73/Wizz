namespace Wizz.Application.AI.Perception;

using Wizz.Application.AI;

/// <summary>
/// يقيس دقة استخراج الاسم والسعر على العينات المحلية دون تدريب أي نموذج.
/// يستخدم نفس مستخرج المرشّحات الذي يستخدمه القارئ، ويحتسب أثر الأسماء المتعلّمة محليًا.
/// </summary>
public sealed class OcrEvaluationService : IOcrEvaluationService
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<decimal>> NoKnownProducts =
        new Dictionary<string, IReadOnlyList<decimal>>(StringComparer.Ordinal);

    private readonly IOcrEvaluationSampleStore _samples;
    private readonly IOcrAliasStore _aliases;

    public OcrEvaluationService(IOcrEvaluationSampleStore samples, IOcrAliasStore aliases)
    {
        _samples = samples;
        _aliases = aliases;
    }

    public OcrEvaluationResult Run()
    {
        var failures = new List<string>();
        var nameHits = 0;
        var priceHits = 0;

        foreach (var sample in _samples.GetAll())
        {
            var groups = PerceptionCandidateExtractor.ExtractGroups(sample.Text, PerceptionSource.Embedded);
            var (primaries, _) = PerceptionCandidateExtractor.SelectPrimaryReadings(groups, NoKnownProducts);
            var expectedName = TextNormalizer.Normalize(sample.ExpectedProduct);

            var match = primaries.FirstOrDefault(candidate =>
            {
                var name = _aliases.Resolve(candidate.ProductName) ?? candidate.ProductName;
                return string.Equals(TextNormalizer.Normalize(name), expectedName, StringComparison.Ordinal);
            });

            if (match is null)
            {
                failures.Add($"{sample.Id}: لم يُستخرج «{sample.ExpectedProduct}».");
                continue;
            }

            nameHits++;

            if (sample.ExpectedPrice is { } expectedPrice)
            {
                var priceOk = primaries.Any(candidate => candidate.Price is { } price
                    && Math.Abs(price - expectedPrice) < 0.01m
                    && string.Equals(
                        TextNormalizer.Normalize(_aliases.Resolve(candidate.ProductName) ?? candidate.ProductName),
                        expectedName,
                        StringComparison.Ordinal));

                if (priceOk) priceHits++;
                else failures.Add($"{sample.Id}: استُخرج الاسم لكن السعر {expectedPrice:0.##} لم يُطابق.");
            }
        }

        return new OcrEvaluationResult(_samples.GetAll().Count, nameHits, priceHits, failures);
    }
}
