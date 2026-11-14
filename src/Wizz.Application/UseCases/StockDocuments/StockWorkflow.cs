namespace Wizz.Application.UseCases.StockDocuments;

using Wizz.Application.Abstractions;
using Wizz.Domain;

/// <summary>أدوات مشتركة لمعاملات المستندات والتاريخ التجاري.</summary>
internal static class StockWorkflow
{
    /// <summary>يبدأ معاملة إن لم تكن هناك معاملة قائمة، ويعيد هل نحن مالكها.</summary>
    public static async Task<bool> BeginIfNeededAsync(IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (unitOfWork.Db.Database.CurrentTransaction is not null)
            return false;

        await unitOfWork.BeginTransactionAsync(ct);
        return true;
    }

    public static Task CommitIfOwnedAsync(IUnitOfWork unitOfWork, bool ownsTransaction, CancellationToken ct)
        => ownsTransaction ? unitOfWork.CommitTransactionAsync(ct) : Task.CompletedTask;

    public static Task RollbackIfOwnedAsync(IUnitOfWork unitOfWork, bool ownsTransaction, CancellationToken ct)
        => ownsTransaction ? unitOfWork.RollbackTransactionAsync(ct) : Task.CompletedTask;

    /// <summary>التاريخ التجاري في المنطقة الزمنية للمتجر، مع احتياط آمن.</summary>
    public static DateOnly BusinessDate(Store store, DateTime utc)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(store.TimeZone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone));
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(utc);
        }
        catch (InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(utc);
        }
    }

    public static DateOnly Today(Store store) => BusinessDate(store, DateTime.UtcNow);
}
