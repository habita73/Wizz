using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات تكامل لقائمة اقتراحات أسماء الزبائن: توحيد الهجاء العربي،
/// استبعاد المبيعات الملغية، وترتيب الأسماء بحسب ديونها المفتوحة.
/// </summary>
public sealed class CustomerSuggestionTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private SaleRepository _repo = null!;
    private Store _store = null!;
    private User _user = null!;
    private Product _product = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new WizzDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _repo = new SaleRepository(_db);

        _store = new Store("متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000", true);
        _user = new User("cashier", "hash", "كاشير اختبار", UserRole.Cashier);
        _product = new Product("منتج اختبار", 100m, 80m, false, "TEST-001");
        _product.AddUnit("وحدة", "وحدة", 1m);
        await _db.Stores.AddAsync(_store);
        await _db.Users.AddAsync(_user);
        await _db.Products.AddAsync(_product);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void CustomerNameRules_TreatsArabicSpellingVariantsAsOne()
    {
        Assert.True(CustomerNameRules.IsSame("أمين", "امين"));
        Assert.True(CustomerNameRules.IsSame("آمين", "أمين"));
        Assert.True(CustomerNameRules.IsSame("سلمى", "سلمي"));
        Assert.True(CustomerNameRules.IsSame(" فاطمّة ", "فاطمه"));
        Assert.False(CustomerNameRules.IsSame("أمين", "سلمى"));
        Assert.False(CustomerNameRules.IsSame(null, "أمين"));
    }

    [Fact]
    public async Task Suggestions_MergeSpellingVariants_AndUseMostCommonSpelling()
    {
        await SeedSaleAsync("أمين");
        await SeedSaleAsync("أمين");
        await SeedSaleAsync("امين");
        await SeedSaleAsync("آمين");

        var result = await _repo.GetCustomerSuggestionsAsync(null);

        var suggestion = Assert.Single(result);
        Assert.Equal("أمين", suggestion.Name);
        Assert.Equal(4, suggestion.SaleCount);
        Assert.Equal(4, suggestion.OpenDebtCount);
    }

    [Fact]
    public async Task Suggestions_MatchTypedTextWithArabicNormalization()
    {
        await SeedSaleAsync("أمين");
        await SeedSaleAsync("سلمى");
        await SeedSaleAsync("كريم");

        var byAlef = await _repo.GetCustomerSuggestionsAsync("امي");
        Assert.Single(byAlef);
        Assert.Equal("أمين", byAlef[0].Name);

        var byMaksura = await _repo.GetCustomerSuggestionsAsync("سلمي");
        Assert.Single(byMaksura);
        Assert.Equal("سلمى", byMaksura[0].Name);

        var noMatch = await _repo.GetCustomerSuggestionsAsync("غيرموجود");
        Assert.Empty(noMatch);
    }

    [Fact]
    public async Task Suggestions_ExcludeCancelled_AndCountOnlyOpenDebts()
    {
        await SeedSaleAsync("سلمى");
        await SeedSaleAsync("سلمى", fullyPaid: true);
        await SeedSaleAsync("سلمى", cancelled: true);

        var result = await _repo.GetCustomerSuggestionsAsync(null);

        var suggestion = Assert.Single(result);
        Assert.Equal("سلمى", suggestion.Name);
        Assert.Equal(2, suggestion.SaleCount);
        Assert.Equal(1, suggestion.OpenDebtCount);
    }

    [Fact]
    public async Task Suggestions_OrderByOpenDebtCount_AndRespectLimit()
    {
        await SeedSaleAsync("كريم");
        await SeedSaleAsync("كريم");
        await SeedSaleAsync("كريم");
        await SeedSaleAsync("عمر");
        await SeedSaleAsync("عمر");
        await SeedSaleAsync("ليلى");

        var result = await _repo.GetCustomerSuggestionsAsync(null, limit: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal("كريم", result[0].Name);
        Assert.Equal("عمر", result[1].Name);
    }

    private async Task SeedSaleAsync(string? customerName, bool fullyPaid = false, bool cancelled = false)
    {
        var unit = _product.GetBaseUnit()!;
        var item = new SaleItem(_product.Id, unit.Id, 1m, _product.RetailPrice, false);
        item.SetCatalog(_product, unit);

        var sale = new Sale(
            _store.Id, _user.Id, new[] { item }, 0m,
            fullyPaid ? _product.RetailPrice : 0m,
            PaymentMethod.Credit, isCreditSale: true,
            dueDate: DateTime.Today.AddDays(20),
            customerName: customerName);

        if (cancelled)
            sale.Cancel("اختبار الاقتراحات", _user.Id);

        await _db.Sales.AddAsync(sale);
        await _db.SaveChangesAsync();
    }
}
