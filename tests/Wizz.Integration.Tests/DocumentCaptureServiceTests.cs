using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.AI.Perception;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات طبقة إدخال ناتج التعرّف: تحكيم المدمج لمرشّحات نص مُدخل، ببيانات اصطناعية،
/// مع إثبات عدم وجود أي كتابة في قاعدة البيانات.
/// </summary>
public sealed class DocumentCaptureServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private UnitOfWork _unitOfWork = null!;
    private DocumentCaptureService _service = null!;

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
        await _unitOfWork.Store.AddAsync(
            new Store("متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000", true));
        await _unitOfWork.SaveChangesAsync();
        _service = new DocumentCaptureService(_unitOfWork.Products);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Capture_AcceptsKnownProductWithMatchingPrice_AndNeverWrites()
    {
        await SeedProductAsync("حليب", 12.5m, 10m);

        var result = await _service.CaptureAsync(new DocumentCaptureRequest("حليب 12.5"));

        Assert.Equal(1, result.Adjudication.AcceptedCount);
        Assert.Equal(0, result.Adjudication.RejectedCount);
        Assert.True(result.Adjudication.Candidates[0].Accepted);
        Assert.Equal(PerceptionAdjudicator.AcceptedReason, result.Adjudication.Candidates[0].Reason);
        Assert.False(result.WrittenToDatabase);
        Assert.Equal(PerceptionAdjudicator.NoWriteReason, result.Warning);
        Assert.Equal(1, await _unitOfWork.Products.CountAsync());
    }

    [Fact]
    public async Task Capture_AcceptsWholesalePrice_FromStoreData()
    {
        await SeedProductAsync("حليب", 15m, 10m);

        var result = await _service.CaptureAsync(new DocumentCaptureRequest("حليب 10"));

        Assert.Equal(1, result.Adjudication.AcceptedCount);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public async Task Capture_RejectsUnknownProductAndPriceMismatch()
    {
        await SeedProductAsync("حليب", 12.5m, 10m);

        var result = await _service.CaptureAsync(new DocumentCaptureRequest("خبز 5\nحليب 99"));

        Assert.Equal(0, result.Adjudication.AcceptedCount);
        Assert.Equal(2, result.Adjudication.RejectedCount);
        Assert.All(result.Adjudication.Candidates, c => Assert.False(c.Accepted));
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, result.Adjudication.Candidates[0].Reason);
        Assert.Equal(PerceptionAdjudicator.PriceMismatchReason, result.Adjudication.Candidates[1].Reason);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public async Task Capture_IgnoresInactiveProducts()
    {
        var product = await SeedProductAsync("حليب", 12.5m, 10m);
        product.UpdateDetails("حليب", 12.5m, 10m, isActive: false, isService: false);
        await _unitOfWork.SaveChangesAsync();

        var result = await _service.CaptureAsync(new DocumentCaptureRequest("حليب 12.5"));

        Assert.Equal(0, result.Adjudication.AcceptedCount);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, result.Adjudication.Candidates[0].Reason);
    }

    [Fact]
    public async Task Capture_EmptyText_ReturnsNoCandidates_AndNeverWrites()
    {
        var result = await _service.CaptureAsync(new DocumentCaptureRequest("   "));

        Assert.Empty(result.Adjudication.Candidates);
        Assert.Equal(0, result.Adjudication.AcceptedCount);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public async Task Capture_CloudSource_GainsNoExtraTrust()
    {
        await SeedProductAsync("حليب", 12.5m, 10m);

        var result = await _service.CaptureAsync(
            new DocumentCaptureRequest("خبز 5", PerceptionSource.Cloud));

        Assert.False(result.Adjudication.Candidates[0].Accepted);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, result.Adjudication.Candidates[0].Reason);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public async Task Capture_DenseRow_JudgesOnePrimaryAndKeepsOtherReadingsUnjudged()
    {
        await SeedProductAsync("تفاح", 12.5m, 10m);

        var result = await _service.CaptureAsync(
            new DocumentCaptureRequest("تفاح أحمر كغ 1.000 12.5 450.00"));

        // صف مكتظ واحد ← مرشّح أساسي واحد يدخل التحكيم، ويُختار الاسم المعروف من بيانات المتجر.
        var verdict = Assert.Single(result.Adjudication.Candidates);
        Assert.Equal("تفاح", verdict.Candidate.ProductName);
        Assert.True(verdict.Accepted);
        Assert.Equal(PerceptionAdjudicator.AcceptedReason, verdict.Reason);

        // بقية الصيغ محفوظة للمراجعة بلا حكم، ولا تدخل أعداد المدمج.
        Assert.NotEmpty(result.Adjudication.PossibleReadings);
        Assert.DoesNotContain(result.Adjudication.PossibleReadings,
            item => item.ProductName == "تفاح" && item.Price == 12.5m);
        Assert.Equal(1, result.Adjudication.AcceptedCount);
        Assert.Equal(0, result.Adjudication.RejectedCount);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public async Task Capture_NumberFirstRow_KeepsEveryProductAsPrimary()
    {
        await SeedProductAsync("حليب", 12.5m, 10m);
        await SeedProductAsync("خبز", 5m, 4m);

        var result = await _service.CaptureAsync(
            new DocumentCaptureRequest("12.5 حليب 5 خبز 150 تفاح"));

        // هذا النمط منتجات مختلفة فعلًا في سطر واحد؛ لا يُسقط منها شيء.
        Assert.Equal(3, result.Adjudication.Candidates.Count);
        Assert.Empty(result.Adjudication.PossibleReadings);
        Assert.Equal(2, result.Adjudication.AcceptedCount);
        Assert.Equal(1, result.Adjudication.RejectedCount);
        Assert.False(result.WrittenToDatabase);
    }

    private async Task<Product> SeedProductAsync(string name, decimal retail, decimal wholesale)
    {
        var product = new Product(name, retail, wholesale, false, $"BAR-{Guid.NewGuid():N}");
        await _unitOfWork.Products.AddAsync(product);
        await _unitOfWork.SaveChangesAsync();
        return product;
    }
}
