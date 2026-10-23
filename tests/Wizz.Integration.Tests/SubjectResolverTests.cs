using Wizz.Application.AI.Memory;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات استخراج موضوع السؤال (اسم المنتج): الكلمات الشائعة تُحذف بمطابقة كاملة،
/// حتى لا تُحذف أسماء منتجات تحتوي حروفها على كلمة شائعة (حليب/مناديل/دقيق).
/// </summary>
public sealed class SubjectResolverTests
{
    [Theory]
    [InlineData("كم سعر حليب", "حليب")]
    [InlineData("سعر مناديل", "مناديل")]
    [InlineData("بكم الدقيق", "الدقيق")]
    [InlineData("كم ثمن تفاح أخضر", "تفاح")]
    public void ExtractTokens_KeepsProductWords_ThatContainStopWordLetters(string question, string expectedWord)
    {
        var tokens = SubjectResolver.ExtractTokens(question);

        Assert.Contains(expectedWord, tokens);
    }

    [Fact]
    public void ExtractTokens_RemovesRealStopWords()
    {
        var tokens = SubjectResolver.ExtractTokens("كم سعر هذا المنتج؟");

        Assert.DoesNotContain("سعر", tokens);
        Assert.DoesNotContain("كم", tokens);
        Assert.DoesNotContain("هذا", tokens);
        Assert.DoesNotContain("المنتج", tokens);
    }

    [Fact]
    public void ExtractTokens_HandlesDefiniteArticle()
    {
        var tokens = SubjectResolver.ExtractTokens("كم سعر المنتجات المعطلة");

        Assert.DoesNotContain("المنتجات", tokens);
    }
}
