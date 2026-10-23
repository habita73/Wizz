using Wizz.Application.AI;
using Wizz.Application.AI.Memory;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات تفضيلات المستخدم (نوع السعر، وحدة القياس، مفتاح الخصوصية)
/// واستخراج موضوع السؤال من قائمة المنتجات.
/// </summary>
public sealed class UserPreferenceTests
{
    // ---- تفضيلات المستخدم ----

    [Fact]
    public void Defaults_AreRetailNoUnitAndContextRemembered()
    {
        var prefs = new UserPreferenceStore();

        Assert.Equal(PriceMode.Retail, prefs.PriceMode);
        Assert.False(prefs.IsWholesalePreferred);
        Assert.Null(prefs.PreferredUnit);
        Assert.True(prefs.RememberContext);
    }

    [Fact]
    public void SetPriceMode_Wholesale_SwitchesPreference()
    {
        var prefs = new UserPreferenceStore();

        prefs.SetPriceMode(PriceMode.Wholesale);

        Assert.True(prefs.IsWholesalePreferred);
    }

    [Fact]
    public void SetPreferredUnit_Blank_ClearsIt()
    {
        var prefs = new UserPreferenceStore();
        prefs.SetPreferredUnit("كجم");

        prefs.SetPreferredUnit("   ");

        Assert.Null(prefs.PreferredUnit);
    }

    [Fact]
    public void SetPreferredUnit_LongValue_IsTruncated()
    {
        var prefs = new UserPreferenceStore();

        prefs.SetPreferredUnit(new string('ا', UserPreferenceStore.MaxUnitLength + 10));

        Assert.Equal(UserPreferenceStore.MaxUnitLength, prefs.PreferredUnit!.Length);
    }

    [Fact]
    public void SetRememberContext_False_DisablesContext()
    {
        var prefs = new UserPreferenceStore();

        prefs.SetRememberContext(false);

        Assert.False(prefs.RememberContext);
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        var prefs = new UserPreferenceStore();
        prefs.SetPriceMode(PriceMode.Wholesale);
        prefs.SetPreferredUnit("كجم");
        prefs.SetRememberContext(false);

        prefs.Reset();

        Assert.Equal(PriceMode.Retail, prefs.PriceMode);
        Assert.Null(prefs.PreferredUnit);
        Assert.True(prefs.RememberContext);
    }

    [Fact]
    public void ToSnapshot_AndFromSnapshot_RoundTrip()
    {
        var prefs = new UserPreferenceStore();
        prefs.SetPriceMode(PriceMode.Wholesale);
        prefs.SetPreferredUnit("كجم");
        prefs.SetRememberContext(false);

        var restored = UserPreferenceStore.FromSnapshot(prefs.ToSnapshot());

        Assert.Equal(PriceMode.Wholesale, restored.PriceMode);
        Assert.Equal("كجم", restored.PreferredUnit);
        Assert.False(restored.RememberContext);
    }

    [Fact]
    public void FromSnapshot_Null_ReturnsDefaults()
    {
        var prefs = UserPreferenceStore.FromSnapshot(null);

        Assert.Equal(PriceMode.Retail, prefs.PriceMode);
        Assert.True(prefs.RememberContext);
    }

    [Fact]
    public void FromSnapshot_InvalidValues_AreIgnored()
    {
        var snapshot = new Dictionary<string, string>
        {
            [UserPreferenceStore.PriceModeKey] = "غير معروف",
            [UserPreferenceStore.PreferredUnitKey] = "   ",
            [UserPreferenceStore.RememberContextKey] = "نعم"
        };

        var prefs = UserPreferenceStore.FromSnapshot(snapshot);

        Assert.Equal(PriceMode.Retail, prefs.PriceMode);
        Assert.Null(prefs.PreferredUnit);
        Assert.True(prefs.RememberContext);
    }

    // ---- استخراج الموضوع ----

    private static readonly IReadOnlyList<ProductInfo> Products = new List<ProductInfo>
    {
        new("تفاح أحمر", "FRUIT-001", 150m, 120m, "كجم", true),
        new("حليب كامل الدسم", "DAIRY-001", 120m, 100m, "لتر", true),
        new("خبز أبيض", "BAKERY-001", 30m, 25m, "رغيف", true)
    };

    [Fact]
    public void FindSubject_WithProductName_ReturnsProductName()
    {
        var subject = SubjectResolver.FindSubject("كم سعر تفاح أحمر؟", Products);

        Assert.Equal("تفاح أحمر", subject);
    }

    [Fact]
    public void FindSubject_WithoutProductName_ReturnsNull()
    {
        var subject = SubjectResolver.FindSubject("كم سعره؟", Products);

        Assert.Null(subject);
    }

    [Fact]
    public void FindSubject_IgnoresQuestionWords()
    {
        var subject = SubjectResolver.FindSubject("بكم سعر الخبز الأبيض", Products);

        Assert.Equal("خبز أبيض", subject);
    }

    [Fact]
    public void FindSubject_WithBarcode_ReturnsProductName()
    {
        var subject = SubjectResolver.FindSubject("سعر FRUIT-001", Products);

        Assert.Equal("تفاح أحمر", subject);
    }

    [Fact]
    public void IsFollowUp_PriceQuestionWithoutSubject_IsTrue()
    {
        Assert.True(SubjectResolver.IsFollowUp("كم سعره؟", Products));
        Assert.True(SubjectResolver.IsFollowUp("وفي الجملة؟", Products));
    }

    [Fact]
    public void IsFollowUp_WithExplicitProduct_IsFalse()
    {
        Assert.False(SubjectResolver.IsFollowUp("كم سعر تفاح أحمر؟", Products));
    }

    [Fact]
    public void ExtractTokens_RemovesCommonQuestionWords()
    {
        var tokens = SubjectResolver.ExtractTokens("كم سعر تفاح أحمر من فضلك");

        Assert.DoesNotContain("سعر", tokens);
        Assert.DoesNotContain("كم", tokens);
        Assert.Contains("تفاح", tokens);
    }

    // ---- التطبيع ----

    [Fact]
    public void Normalize_UnifiesArabicVariants()
    {
        Assert.Equal(TextNormalizer.Normalize("أحمد"), TextNormalizer.Normalize("احمد"));
        Assert.Equal(TextNormalizer.Normalize("مدرسة"), TextNormalizer.Normalize("مدرسه"));
    }

    [Fact]
    public void Normalize_ConvertsArabicDigits()
    {
        Assert.Equal("2026", TextNormalizer.Normalize("٢٠٢٦"));
    }

    [Fact]
    public void Normalize_RemovesDiacritics()
    {
        Assert.Equal("سعر", TextNormalizer.Normalize("سَعْر"));
    }

    [Fact]
    public void HasAny_MatchesSubstring()
    {
        Assert.True(TextNormalizer.HasAny("مجموع ديون اليوم", "ديون"));
        Assert.False(TextNormalizer.HasAny("مجموع ديون اليوم", "منتج"));
    }
}
