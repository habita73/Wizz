using Wizz.Domain;

namespace Wizz.Domain.Tests;

public sealed class SaleCalculatorTests
{
    [Fact]
    public void Calculate_SingleLine_ReturnsExpectedTotals()
    {
        var result = SaleCalculator.Calculate([new SaleLine(12.50m, 2m)], 0m, 10m);

        Assert.Equal(25.00m, result.Subtotal);
        Assert.Equal(0m, result.Discount);
        Assert.Equal(25.00m, result.Total);
        Assert.Equal(10.00m, result.Paid);
        Assert.Equal(15.00m, result.Remaining);
    }

    [Fact]
    public void Calculate_FractionalQuantity_ReturnsExpectedTotals()
    {
        var result = SaleCalculator.Calculate([new SaleLine(12.50m, 1.5m)], 0m, 0m);

        Assert.Equal(18.75m, result.Subtotal);
        Assert.Equal(18.75m, result.Total);
        Assert.Equal(18.75m, result.Remaining);
    }

    [Fact]
    public void Calculate_QuantityWithMoreThanTwoDecimalPlaces_IsAllowed()
    {
        var result = SaleCalculator.Calculate([new SaleLine(3m, 0.333m)], 0m, 0m);

        Assert.Equal(1.00m, result.Subtotal);
    }

    [Fact]
    public void Calculate_MultipleLines_WithFixedDiscount_ReturnsAccurateBalance()
    {
        var result = SaleCalculator.Calculate(
            [new SaleLine(12.50m, 2m), new SaleLine(3.75m, 3m)],
            5m,
            20m);

        Assert.Equal(36.25m, result.Subtotal);
        Assert.Equal(5.00m, result.Discount);
        Assert.Equal(31.25m, result.Total);
        Assert.Equal(20.00m, result.Paid);
        Assert.Equal(11.25m, result.Remaining);
    }

    [Fact]
    public void Calculate_RoundsEachLineAwayFromZero_BeforeSumming()
    {
        var result = SaleCalculator.Calculate(
            [new SaleLine(0.01m, 0.5m), new SaleLine(0.01m, 0.5m)],
            0m,
            0m);

        Assert.Equal(0.02m, result.Subtotal);
        Assert.Equal(0.02m, result.Total);
    }

    [Fact]
    public void Calculate_RoundsMidpointsAwayFromZero_ToTwoDecimalPlaces()
    {
        var result = SaleCalculator.Calculate([new SaleLine(1.01m, 0.5m)], 0m, 0m);

        Assert.Equal(0.51m, result.Subtotal);
        Assert.Equal(0.51m, result.Total);
        Assert.Equal(0.51m, result.Remaining);
    }

    [Fact]
    public void Calculate_DiscountEqualToSubtotal_AllowsZeroTotalSale()
    {
        var result = SaleCalculator.Calculate([new SaleLine(10m, 1m)], 10m, 0m);

        Assert.Equal(0m, result.Total);
        Assert.Equal(0m, result.Paid);
        Assert.Equal(0m, result.Remaining);
    }

    [Fact]
    public void Calculate_PaymentEqualToTotal_AllowsFullyPaidSale()
    {
        var result = SaleCalculator.Calculate([new SaleLine(10m, 1m)], 0m, 10m);

        Assert.Equal(10m, result.Paid);
        Assert.Equal(0m, result.Remaining);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void SaleLine_InvalidPriceOrQuantity_IsRejected(decimal unitPrice, decimal quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SaleLine(unitPrice, quantity));
    }

    [Fact]
    public void SaleLine_PriceWithMoreThanTwoDecimalPlaces_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SaleLine(1.001m, 1m));
    }

    [Fact]
    public void Calculate_DiscountWithMoreThanTwoDecimalPlaces_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaleCalculator.Calculate([new SaleLine(10m, 1m)], 0.001m, 0m));
    }

    [Fact]
    public void Calculate_PaymentWithMoreThanTwoDecimalPlaces_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaleCalculator.Calculate([new SaleLine(10m, 1m)], 0m, 0.001m));
    }

    [Fact]
    public void Calculate_NegativeDiscount_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaleCalculator.Calculate([new SaleLine(10m, 1m)], -0.01m, 0m));
    }

    [Fact]
    public void Calculate_DiscountAboveSubtotal_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaleCalculator.Calculate([new SaleLine(10m, 1m)], 10.01m, 0m));
    }

    [Fact]
    public void Calculate_NegativePayment_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaleCalculator.Calculate([new SaleLine(10m, 1m)], 0m, -0.01m));
    }

    [Fact]
    public void Calculate_PaymentAboveTotal_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaleCalculator.Calculate([new SaleLine(10m, 1m)], 0m, 10.01m));
    }

    [Fact]
    public void Calculate_EmptySale_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => SaleCalculator.Calculate([], 0m, 0m));
    }

    [Fact]
    public void Calculate_NullSale_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SaleCalculator.Calculate(null!, 0m, 0m));
    }

    [Fact]
    public void Calculate_NullLineEntry_IsRejected()
    {
        IReadOnlyCollection<SaleLine> lines = new List<SaleLine> { null! };

        Assert.Throws<ArgumentException>(() => SaleCalculator.Calculate(lines, 0m, 0m));
    }

    [Fact]
    public void Calculate_Overflow_IsReportedAsDomainError()
    {
        var line = new SaleLine(decimal.MaxValue, 2m);

        Assert.Throws<SaleCalculationException>(() => SaleCalculator.Calculate([line], 0m, 0m));
    }

    [Fact]
    public void Calculate_SubtotalOverflow_IsReportedAsDomainError()
    {
        var line = new SaleLine(decimal.MaxValue, 1m);

        Assert.Throws<SaleCalculationException>(() => SaleCalculator.Calculate([line, line], 0m, 0m));
    }
}