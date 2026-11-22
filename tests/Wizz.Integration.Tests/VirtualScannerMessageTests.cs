using Wizz.Application.Devices;

namespace Wizz.Integration.Tests;

public class VirtualScannerMessageTests
{
    [Fact]
    public void Parse_ReadsScanCodeAndQuantity()
    {
        var message = VirtualScanMessage.Parse(
            "{\"type\":\"scan\",\"code\":\" FRUIT-001 \",\"quantity\":2.5}");

        Assert.NotNull(message);
        Assert.Equal("scan", message!.Type);
        Assert.Equal("FRUIT-001", message.Code);
        Assert.Equal(2.5m, message.Quantity);
    }

    [Fact]
    public void Parse_DefaultsQuantityToOne_WhenMissingOrNotPositive()
    {
        var missing = VirtualScanMessage.Parse("{\"type\":\"scan\",\"code\":\"DAIRY-001\"}");
        var zero = VirtualScanMessage.Parse("{\"type\":\"scan\",\"code\":\"DAIRY-001\",\"quantity\":0}");

        Assert.NotNull(missing);
        Assert.Equal(1m, missing!.Quantity);
        Assert.NotNull(zero);
        Assert.Equal(1m, zero!.Quantity);
    }

    [Fact]
    public void Parse_ReadsAcknowledgementFlags()
    {
        var message = VirtualScanMessage.Parse(
            "{\"type\":\"scan-ack\",\"code\":\"BAKERY-001\",\"productName\":\"خبز\",\"ok\":true}");

        Assert.NotNull(message);
        Assert.Equal("scan-ack", message!.Type);
        Assert.Equal("خبز", message.ProductName);
        Assert.True(message.Ok);
    }

    [Fact]
    public void Parse_ReturnsNull_ForInvalidPayloads()
    {
        Assert.Null(VirtualScanMessage.Parse(null));
        Assert.Null(VirtualScanMessage.Parse("{not-json"));
        Assert.Null(VirtualScanMessage.Parse("[]"));
        Assert.Null(VirtualScanMessage.Parse("{\"code\":\"FRUIT-001\"}"));
    }
}
