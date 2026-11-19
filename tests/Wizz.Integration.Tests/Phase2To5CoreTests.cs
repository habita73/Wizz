using Wizz.Application.Abstractions;
using Wizz.Application.AI;
using Wizz.Application.AI.Orchestration;
using Wizz.Application.AI.Perception;
using Wizz.Application.AI.Privacy;
using Wizz.Application.AI.Semantic;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات المراحل ٢…٥ من خارطة المساعد (بيانات اصطناعية فقط):
/// تحكيم المدمج للأرقام، أنماط العمل، التنقيح قبل أي تشارك،
/// تحكيم نتائج التعرّف بلا كتابة في قاعدة البيانات، والذاكرة الدلالية المحلية.
/// </summary>
public sealed class Phase2To5CoreTests
{
    // ===== المرحلة ٢: أنماط العمل وتحكيم الأرقام =====

    [Fact]
    public void NumberQuestion_IsSolo_AndBuiltInIsSoleContributor()
    {
        var builtIn = new BuiltInAnswer("مبيعات اليوم 1500 دج", "مبيعات اليوم", "مبيعات");

        var decision = WorkModeResolver.Resolve("ما هي مبيعات اليوم؟", builtIn, localAvailable: true, cloudAvailable: true);

        Assert.Equal(WorkMode.Solo, decision.Mode);
        Assert.Equal(WorkModeResolver.PrecisionReason, decision.Reason);
        Assert.Equal(new[] { RouteType.BuiltIn }, decision.Contributors);
        Assert.False(decision.IsManual);
    }

    [Fact]
    public void PhrasingTask_WithModel_IsDelegated()
    {
        var decision = WorkModeResolver.Resolve(
            "صيغ هذا الكلام",
            builtInAnswer: null,
            localAvailable: true,
            cloudAvailable: false);

        Assert.Equal(WorkMode.Delegated, decision.Mode);
        Assert.Contains(RouteType.BuiltIn, decision.Contributors);
        Assert.Contains(RouteType.Local, decision.Contributors);
        Assert.DoesNotContain(RouteType.Cloud, decision.Contributors);
    }

    [Fact]
    public void ComplexTask_WithModel_IsShared()
    {
        var decision = WorkModeResolver.Resolve(
            "حلل المبيعات وصيغ تقريراً",
            builtInAnswer: null,
            localAvailable: true,
            cloudAvailable: true);

        Assert.Equal(WorkMode.Shared, decision.Mode);
        Assert.Equal(3, decision.Contributors.Count);
    }

    [Fact]
    public void ManualRoute_IsRespected_AndFlagged()
    {
        var decision = WorkModeResolver.Resolve(
            "اشرح لي شيئاً",
            builtInAnswer: null,
            localAvailable: true,
            cloudAvailable: false,
            isManual: true,
            manualRoute: RouteType.Local);

        Assert.True(decision.IsManual);
        Assert.Equal(WorkMode.Delegated, decision.Mode);
        Assert.Contains(RouteType.Local, decision.Contributors);
    }

    [Fact]
    public void NumberAdjudicator_RejectsForeignNumber_AndRestoresBuiltInText()
    {
        var facts = new FactPackage("مبيعات اليوم 1500 دج", new[] { "1500" }, "مبيعات", null);

        var verdict = NumberAdjudicator.Adjudicate("مبيعات اليوم 9999 دج بعد التنسيق", facts);

        Assert.False(verdict.Accepted);
        Assert.Equal(facts.AuthoritativeText, verdict.FinalText);
        Assert.Contains("9999", verdict.ForeignNumbers);
        Assert.Equal(NumberAdjudicator.RejectedReason, verdict.Reason);
    }

    [Fact]
    public void NumberAdjudicator_AcceptsDraft_WhenAllNumbersComeFromBuiltIn()
    {
        var facts = new FactPackage("مبيعات اليوم 1500 دج", new[] { "1500" }, "مبيعات", null);

        var verdict = NumberAdjudicator.Adjudicate("إجمالي مبيعات اليوم بلغ 1500 دج", facts);

        Assert.True(verdict.Accepted);
        Assert.Equal("إجمالي مبيعات اليوم بلغ 1500 دج", verdict.FinalText);
        Assert.Empty(verdict.ForeignNumbers);
    }

    [Fact]
    public void FactPackage_FromNullAnswer_IsEmpty()
    {
        var facts = FactPackage.FromBuiltIn(null);

        Assert.False(facts.HasNumbers);
        Assert.Equal(string.Empty, facts.AuthoritativeText);
        Assert.Empty(facts.NumericTokens);
    }

    // ===== المرحلة ٣: التشارك السحابي الآمن =====

    [Theory]
    [InlineData("اسم العميل أحمد")]
    [InlineData("رقم الجوال 0771234567")]
    [InlineData("رقم الفاتورة INV-2024-99")]
    [InlineData("العنوان شارع الاستقلال")]
    [InlineData("نسخة كاملة من قاعدة البيانات")]
    [InlineData("بريد test@example.com")]
    public void RedactionValidator_BlocksForbiddenData(string unsafeText)
    {
        Assert.False(RedactionValidator.IsSafe(unsafeText));
    }

    [Theory]
    [InlineData("استفسار مبيعات مجمعة — نطاق زمني واسع: الشهر.")]
    [InlineData("ملخص مجهول لتحليل متوسط الأسعار")]
    [InlineData("اتجاه عام للمبيعات دون تفاصيل")]
    public void RedactionValidator_AllowsAggregateSummaries(string safeText)
    {
        Assert.True(RedactionValidator.IsSafe(safeText));
    }

    [Fact]
    public void QuestionRedactor_ProducesSafeSummary_AndDropsRawIdentifiers()
    {
        var redacted = QuestionRedactor.Redact("كم مبيعات الزبون أحمد رقم الهاتف 0771234567 فاتورة رقم INV-2024-77؟");

        Assert.True(redacted.IsSafe);
        Assert.NotEmpty(redacted.RemovedCategories);
        Assert.Contains("مبيعات", redacted.Summary);
        Assert.DoesNotContain("أحمد", redacted.Summary);
        Assert.DoesNotContain("0771234567", redacted.Summary);
        Assert.DoesNotContain("INV", redacted.Summary);
    }

    [Fact]
    public void CloudShareGuard_WhenSharingDisabled_ReturnsNull_NoPayload()
    {
        var settings = new FakeSettings { CloudSharingEnabled = false };
        var guard = new CloudShareGuard(settings);

        var payload = guard.TryPrepare("كم مبيعات اليوم؟");

        Assert.Null(payload);
    }

    [Fact]
    public void CloudShareGuard_WhenSharingEnabled_ReturnsRedactedPayloadOnly()
    {
        var settings = new FakeSettings { CloudSharingEnabled = true };
        var guard = new CloudShareGuard(settings);

        var payload = guard.TryPrepare("اسم العميل أحمد كم مبيعات اليوم؟");

        Assert.NotNull(payload);
        Assert.Equal(CloudShareGuard.AllowedPurpose, payload.Purpose);
        Assert.DoesNotContain("أحمد", payload.RedactedSummary);
        Assert.True(RedactionValidator.IsSafe(payload.RedactedSummary));
    }

    [Fact]
    public void CloudPromptGuard_BlocksUnsafe_AndAllowsSafe()
    {
        Assert.False(CloudPromptGuard.IsSafeToSend("ملخص آمن", "اسم العميل أحمد"));
        Assert.True(CloudPromptGuard.IsSafeToSend("ملخص مبيعات مجمّع", "استفسار أسعار — نطاق واسع"));
    }

    [Fact]
    public void ApprovedPrivacyWording_IsExact()
    {
        Assert.Equal(
            "عند تفعيل التشارك السحابي فقط تُرسل ملخصات مجهولة الهوية — أرقام مجمّعة بلا أسماء عملاء ولا معرّفات فواتير — لتحسين الصياغة والتحليل. ولا يُرسل سجل المتجر ولا قاعدة البيانات كاملة. وبدون التفعيل يعمل المساعد كاملًا دون إنترنت.",
            ApprovedPrivacyWording.Text);
    }

    // ===== المرحلة ٤: الكاميرا والمستندات، بلا كتابة في قاعدة البيانات =====

    [Fact]
    public void PerceptionExtractor_ParsesProductAndPrice()
    {
        var candidates = PerceptionCandidateExtractor.Extract("حليب طازج 12.5", PerceptionSource.Embedded);

        var candidate = Assert.Single(candidates);
        Assert.Equal("حليب طازج", candidate.ProductName);
        Assert.Equal(12.5m, candidate.Price);
        Assert.Equal(PerceptionSource.Embedded, candidate.Source);
    }

    [Fact]
    public void PerceptionExtractor_WindowsOcrNumberFirst_MapsEachPriceToItsFollowingName()
    {
        // نمط إخراج قرة Windows: الرقم قبل الاسم، وعدة منتجات في سطر واحد.
        var candidates = PerceptionCandidateExtractor.Extract("12.5 حليب 5 خبز 150 تفاح");

        Assert.Equal(3, candidates.Count);
        Assert.Equal("حليب", candidates[0].ProductName);
        Assert.Equal(12.5m, candidates[0].Price);
        Assert.Equal("خبز", candidates[1].ProductName);
        Assert.Equal(5m, candidates[1].Price);
        Assert.Equal("تفاح", candidates[2].ProductName);
        Assert.Equal(150m, candidates[2].Price);
    }

    [Fact]
    public void PerceptionAdjudicator_WindowsOcrNumberFirst_AcceptsKnownAndRejectsUnknown()
    {
        var candidates = PerceptionCandidateExtractor.Extract("12.5 حليب 7 مجهول");
        var known = new Dictionary<string, IReadOnlyList<decimal>>
        {
            ["حليب"] = new[] { 12.5m }
        };

        var result = PerceptionAdjudicator.Adjudicate(candidates, known);

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.True(result.Candidates[0].Accepted);
        Assert.False(result.Candidates[1].Accepted);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, result.Candidates[1].Reason);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public void PerceptionAdjudicator_AcceptsKnownProductWithMatchingPrice_AndNeverWrites()
    {
        var candidates = PerceptionCandidateExtractor.Extract("حليب 12.5");
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };

        var result = PerceptionAdjudicator.Adjudicate(candidates, known);

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.True(result.Candidates[0].Accepted);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public void PerceptionAdjudicator_RejectsUnknownProduct_AndPriceMismatch()
    {
        var candidates = PerceptionCandidateExtractor.Extract("خبز 5\nحليب 99");
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };

        var result = PerceptionAdjudicator.Adjudicate(candidates, known);

        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.All(result.Candidates, c => Assert.False(c.Accepted));
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public void PerceptionAdjudicator_CloudSource_GainsNoExtraTrust()
    {
        var candidate = new PerceptionCandidate("خبز", 5m, "خبز 5", Confidence: 1.0, PerceptionSource.Cloud);
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };

        var result = PerceptionAdjudicator.Adjudicate(new[] { candidate }, known);

        Assert.False(result.Candidates[0].Accepted);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, result.Candidates[0].Reason);
    }

    // ===== المرحلة ٥: الفهم الدلالي والذاكرة المحلية =====

    [Theory]
    [InlineData("كم مبيعات اليوم؟", SemanticIntent.Sales)]
    [InlineData("بغيت سعر الحليب", SemanticIntent.Price)]
    [InlineData("شحال دين الزبون", SemanticIntent.Debt)]
    [InlineData("كم الساعة", SemanticIntent.Tools)]
    [InlineData("حلل النتائج", SemanticIntent.Analysis)]
    public void SemanticResolver_DetectsIntent(string question, SemanticIntent expected)
    {
        var understanding = SemanticIntentResolver.Resolve(question);

        Assert.Equal(expected, understanding.Intent);
    }

    [Fact]
    public void SemanticResolver_FollowUp_ResolvesRememberedSubject()
    {
        var understanding = SemanticIntentResolver.Resolve("بشحال؟", rememberedSubject: "حليب");

        Assert.True(understanding.IsFollowUp);
        Assert.Equal("حليب", understanding.ResolvedSubject);
    }

    [Fact]
    public void SemanticMemory_RemembersSubject_AndCapsEntries()
    {
        var memory = new SemanticMemory(enabled: true);

        for (var i = 0; i < SemanticMemory.MaxEntries + 5; i++)
        {
            memory.Remember($"سؤال {i}", SemanticIntentResolver.Resolve("مبيعات اليوم"));
        }

        Assert.Equal(SemanticMemory.MaxEntries, memory.Count);
        Assert.NotNull(memory.LastSubject);
    }

    [Fact]
    public void SemanticMemory_WhenDisabled_DoesNotStore()
    {
        var memory = new SemanticMemory(enabled: false);

        memory.Remember("سؤال", SemanticIntentResolver.Resolve("مبيعات"));

        Assert.Equal(0, memory.Count);
        Assert.Null(memory.LastSubject);
        Assert.Empty(memory.Recent(3));
    }

    /// <summary>إعدادات وهمية للاختبار — التشارك مغلق افتراضياً.</summary>
    private sealed class FakeSettings : IAiSettingsProvider
    {
        public bool IsCloudEnabled { get; set; }
        public bool CloudSharingEnabled { get; set; }
        public string? PreferredLocalModel => null;
        public string LocalModelLevel { get; set; } = "light";
        public string? PreferredCloudProvider => null;
        public int? MonthlyFreeTokenLimit => null;
        public int UsedTokensThisMonth { get; private set; }
        public bool IsQuotaExceeded => false;
        public void RecordTokenUsage(int tokens) => UsedTokensThisMonth += tokens;
        public void Reset() => UsedTokensThisMonth = 0;
        public string? GetCloudApiKey() => null;
        public void SetCloudApiKey(string apiKey) { }
        public string? GetCloudConfigHash() => "test-hash";
        public IEnumerable<CloudProviderConfig>? GetCloudProvidersConfig() => null;
    }
}
