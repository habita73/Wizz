using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.AI.Perception;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات التقاط الصورة: القارئ يُنتج نصًا خامًا فقط، والمحكّم المدمج هو من يقرر،
/// ببيانات اصطناعية، مع إثبات عدم وجود أي كتابة في قاعدة البيانات.
/// </summary>
public sealed class OcrDocumentCaptureServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private UnitOfWork _unitOfWork = null!;

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
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task UnavailableReader_ReturnsNoAdjudication_AndNeverWrites()
    {
        var service = new OcrDocumentCaptureService(
            new FakeReader(OcrReadResult.Unavailable("القارئ غير متاح")),
            new DocumentCaptureService(_unitOfWork.Products));

        var result = await service.CaptureAsync(new byte[] { 1, 2, 3 });

        Assert.False(result.ReaderAvailable);
        Assert.Null(result.Adjudication);
        Assert.Equal("القارئ غير متاح", result.UnavailableReason);
        Assert.False(result.WrittenToDatabase);
        Assert.Equal(0, await _unitOfWork.Products.CountAsync());
    }

    [Fact]
    public async Task AvailableReader_PassesTextThroughEmbeddedAdjudicator_AndNeverWrites()
    {
        await SeedProductAsync("حليب", 12.5m, 10m);
        var service = new OcrDocumentCaptureService(
            new FakeReader(OcrReadResult.Success("حليب 12.5", "ar")),
            new DocumentCaptureService(_unitOfWork.Products));

        var result = await service.CaptureAsync(new byte[] { 1, 2, 3 });

        Assert.True(result.ReaderAvailable);
        Assert.Equal("حليب 12.5", result.RecognizedText);
        Assert.NotNull(result.Adjudication);
        Assert.Equal(1, result.Adjudication.Adjudication.AcceptedCount);
        Assert.Equal(0, result.Adjudication.Adjudication.RejectedCount);
        Assert.False(result.WrittenToDatabase);
        Assert.Equal(PerceptionAdjudicator.NoWriteReason, result.Adjudication.Warning);
        Assert.Equal(1, await _unitOfWork.Products.CountAsync());
    }

    [Fact]
    public async Task ReaderTextWithUnknownProduct_IsRejectedByAdjudicator()
    {
        await SeedProductAsync("حليب", 12.5m, 10m);
        var service = new OcrDocumentCaptureService(
            new FakeReader(OcrReadResult.Success("خبز 5", "ar")),
            new DocumentCaptureService(_unitOfWork.Products));

        var result = await service.CaptureAsync(new byte[] { 1, 2, 3 });

        Assert.True(result.ReaderAvailable);
        Assert.NotNull(result.Adjudication);
        Assert.Equal(0, result.Adjudication.Adjudication.AcceptedCount);
        Assert.Equal(1, result.Adjudication.Adjudication.RejectedCount);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, result.Adjudication.Adjudication.Candidates[0].Reason);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public async Task EmptyOrOversizedImage_IsRejectedBeforeCallingReader()
    {
        var reader = new FakeReader(OcrReadResult.Success("حليب 12.5", "ar"));
        var service = new OcrDocumentCaptureService(reader, new DocumentCaptureService(_unitOfWork.Products));

        var empty = await service.CaptureAsync(Array.Empty<byte>());
        var oversized = await service.CaptureAsync(new byte[OcrDocumentCaptureService.MaxImageBytes + 1]);

        Assert.False(empty.ReaderAvailable);
        Assert.False(oversized.ReaderAvailable);
        Assert.Equal(0, reader.CallCount);
        Assert.False(empty.WrittenToDatabase);
        Assert.False(oversized.WrittenToDatabase);
    }

    private async Task SeedProductAsync(string name, decimal retail, decimal wholesale)
    {
        var product = new Product(name, retail, wholesale, false, $"BAR-{Guid.NewGuid():N}");
        await _unitOfWork.Products.AddAsync(product);
        await _unitOfWork.SaveChangesAsync();
    }

    private sealed class FakeReader : IOcrTextReader
    {
        private readonly OcrReadResult _result;

        public FakeReader(OcrReadResult result) => _result = result;

        public int CallCount { get; private set; }

        public bool IsSupported => _result.IsAvailable;

        public Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
