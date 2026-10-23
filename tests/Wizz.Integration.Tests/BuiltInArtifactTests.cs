using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.AI;
using Wizz.Application.AI.Artifacts;
using Wizz.Application.AI.Tools;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Sales;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات إنتاج المخرجات (Artifacts) من المساعد المدمج:
/// المبيعات والديون والمنتجات وبيانات المتجر يجب أن تُرفق بجدول قابل للتصدير
/// (وسم <c>wizz-artifact</c>) لا مجرد نص.
/// </summary>
public sealed class BuiltInArtifactTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private UnitOfWork _unitOfWork = null!;
    private BuiltInAssistant _assistant = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new WizzDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _unitOfWork = new UnitOfWork(_db);
        _assistant = new BuiltInAssistant(_unitOfWork.Sales, _unitOfWork.Products, _unitOfWork.Store);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SalesSummary_ProducesExportableTable()
    {
        await SeedSaleAsync(isCredit: false, paid: 300m, quantity: 2m);

        var answer = await _assistant.TryAnswerWithSubjectAsync("ما هي مبيعات اليوم؟");

        Assert.NotNull(answer);
        Assert.Equal("مبيعات", answer!.Intent);
        Assert.Contains("```wizz-artifact", answer.Text);

        var artifact = SingleTable(answer.Text, out var text);
        Assert.Equal("ملخص المبيعات", artifact.Title);
        Assert.Equal(5, artifact.Rows.Count);           // اليوم/أمس/آخر 7 أيام/هذا الشهر/كل الفترات
        Assert.Equal(5, artifact.Columns.Count);
        Assert.Contains("مبيعات اليوم", text);          // النص المرئي بقي موجوداً
        Assert.DoesNotContain("wizz-artifact", text);   // الوسم مخفي عن المستخدم
    }

    [Fact]
    public async Task DebtList_ProducesExportableTable()
    {
        await SeedSaleAsync(isCredit: true, paid: 50m, quantity: 2m);

        var answer = await _assistant.TryAnswerWithSubjectAsync("قائمة الديون");

        Assert.NotNull(answer);
        Assert.Equal("ديون", answer!.Intent);

        var artifact = SingleTable(answer.Text, out _);
        Assert.StartsWith("قائمة الديون", artifact.Title);
        Assert.Single(artifact.Rows);
        Assert.Equal(6, artifact.Columns.Count);
        Assert.Equal("رقم الفاتورة", artifact.Columns[1]);
    }

    [Fact]
    public async Task ProductList_ProducesExportableTableWithWholesalePrice()
    {
        await SeedStoreAndProductAsync();

        var answer = await _assistant.TryAnswerWithSubjectAsync("قائمة المنتجات");

        Assert.NotNull(answer);
        Assert.Equal("منتجات", answer!.Intent);

        var artifact = SingleTable(answer.Text, out _);
        Assert.StartsWith("المنتجات والأسعار", artifact.Title);
        Assert.Single(artifact.Rows);
        Assert.Contains("الجملة (د.ج)", artifact.Columns);
        Assert.Contains("120.00", artifact.Rows[0]);
    }

    [Fact]
    public async Task StoreInfo_ProducesExportableTable()
    {
        await SeedStoreAndProductAsync();

        var answer = await _assistant.TryAnswerWithSubjectAsync("ما اسم المتجر؟");

        Assert.NotNull(answer);
        Assert.Equal("متجر", answer!.Intent);

        var artifact = SingleTable(answer.Text, out _);
        Assert.Equal("بيانات المتجر", artifact.Title);
        Assert.Equal(3, artifact.Rows.Count);
    }

    [Fact]
    public async Task PrecisionAnswers_AreMarkedAsProtected()
    {
        await SeedSaleAsync(isCredit: true, paid: 50m, quantity: 2m);

        foreach (var question in new[] { "ما هي مبيعات اليوم؟", "قائمة الديون", "كم سعر تفاح" })
        {
            var answer = await _assistant.TryAnswerWithSubjectAsync(question);
            Assert.True(BuiltInAssistant.IsPrecisionAnswer(answer), $"يجب حماية: {question}");
        }

        var procedural = await _assistant.TryAnswerWithSubjectAsync("كيف أضيف منتج؟");
        Assert.NotNull(procedural);
        Assert.False(BuiltInAssistant.IsPrecisionAnswer(procedural));
    }

    [Fact]
    public async Task SimilarProductNames_ListAllMatchesInsteadOfGuessing()
    {
        var store = new Store("متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000", true);
        await _unitOfWork.Store.AddAsync(store);

        var names = new[] { "حليب كامل الدسم", "حليب نصف الدسم", "حليب بودرة" };
        foreach (var name in names)
        {
            var product = new Product(name, 120m, 100m, false, $"MILK-{name.Length}", "حليب طازج");
            product.AddUnit("لتر", "لتر", 1m);
            await _unitOfWork.Products.AddAsync(product);
        }

        await _unitOfWork.SaveChangesAsync();

        var stored = await _unitOfWork.Products.GetAllAsync();
        Assert.Equal(3, stored.Count);

        var answer = await _assistant.TryAnswerWithSubjectAsync("كم سعر حليب");

        Assert.NotNull(answer);
        Assert.Contains("عدة منتجات", answer!.Text);
        Assert.Equal("سعر", answer.Intent);        foreach (var name in names)
        {
            Assert.Contains(name, answer.Text);
        }
    }

    [Fact]
    public async Task WebSearchAnswer_IsLabelledWithSearchIntent()
    {
        await SeedStoreAndProductAsync();
        var registry = BuiltInToolRegistry.CreateDefault(
            webSearch: new FakeWebSearch(),
            internetAllowed: () => true);
        var assistant = new BuiltInAssistant(
            _unitOfWork.Sales, _unitOfWork.Products, _unitOfWork.Store, registry);

        var answer = await assistant.TryAnswerWithSubjectAsync("من هو ابن باديس؟");

        Assert.NotNull(answer);
        Assert.Equal(SearchWebTool.Intent, answer!.Intent);
        Assert.True(SearchWebTool.IsWebAnswer(answer.Text));
        Assert.Contains("ويكيبيديا", answer.Text);
    }

    [Fact]
    public async Task WebSearchAnswer_IsNotProducedWhenInternetAccessIsOff()
    {
        await SeedStoreAndProductAsync();
        var registry = BuiltInToolRegistry.CreateDefault(
            webSearch: new FakeWebSearch(),
            internetAllowed: () => false);
        var assistant = new BuiltInAssistant(
            _unitOfWork.Sales, _unitOfWork.Products, _unitOfWork.Store, registry);

        var answer = await assistant.TryAnswerWithSubjectAsync("من هو ابن باديس؟");

        Assert.Null(answer);
    }

    private sealed class FakeWebSearch : IWebSearch
    {
        public Task<WebSearchResult?> SearchAsync(string query, CancellationToken ct = default)
            => Task.FromResult<WebSearchResult?>(new WebSearchResult(
                "ويكيبيديا العربية",
                "ملخص تجريبي",
                new[] { new WebSearchSnippet("ابن باديس", "عالم ومصلح جزائري.", "https://ar.wikipedia.org/wiki/ابن_باديس") }));
    }

    /// <summary>يستخرج جدولاً واحداً من نص الرد ويتأكد أن النص المرئي خالٍ من الوسم.</summary>
    private static ChatArtifact SingleTable(string answer, out string cleanText)
    {
        var (text, artifacts) = ArtifactParser.Parse(answer);
        cleanText = text;

        var table = Assert.Single(artifacts);
        Assert.Equal(ArtifactKind.Table, table.Kind);
        return table;
    }

    private async Task<Store> SeedStoreAndProductAsync()
    {
        var store = new Store("متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000", true);
        await _unitOfWork.Store.AddAsync(store);

        var product = new Product("تفاح أحمر", 150m, 120m, false, "FRUIT-001", "تفاح طازج");
        product.AddUnit("كيلوغرام", "كجم", 1m);
        await _unitOfWork.Products.AddAsync(product);
        await _unitOfWork.SaveChangesAsync();
        return store;
    }

    private async Task SeedSaleAsync(bool isCredit, decimal paid, decimal quantity)
    {
        var store = await SeedStoreAndProductAsync();
        var product = (await _unitOfWork.Products.GetAllAsync()).Single();

        var user = new User("cashier", "hash", "كاشير اختبار", UserRole.Cashier);
        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        var useCase = new CreateSaleUseCase(_unitOfWork);
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, quantity, false) },
            Discount: 0m,
            Paid: paid,
            PaymentMethodDto.Cash,
            IsCreditSale: isCredit,
            DueDate: isCredit ? DateTime.Today.AddDays(7) : null);

        var result = await useCase.ExecuteAsync(input, user.Id);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : string.Join(" | ", result.Errors));
        Assert.Equal(store.Id, _db.Sales.Single().StoreId);
    }
}
