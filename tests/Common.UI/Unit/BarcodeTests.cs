namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class BarcodeTests
{
    // ── BarcodeFormat enum ─────────────────────────────────────────────────────
    [Fact]
    public void BarcodeFormatShouldHaveSixValues()
    {
        Enum.GetValues<BarcodeFormat>().Should().HaveCount(6);
    }

    [Fact]
    public void BarcodeFormatShouldContainAllExpectedMembers()
    {
        var values = Enum.GetValues<BarcodeFormat>();
        values.Should().Contain(BarcodeFormat.Code128);
        values.Should().Contain(BarcodeFormat.Ean13);
        values.Should().Contain(BarcodeFormat.Ean8);
        values.Should().Contain(BarcodeFormat.UpcA);
        values.Should().Contain(BarcodeFormat.Code39);
        values.Should().Contain(BarcodeFormat.Itf14);
    }

    // ── FormatKey mapping ──────────────────────────────────────────────────────
    [Theory]
    [InlineData(BarcodeFormat.Code128, "CODE128")]
    [InlineData(BarcodeFormat.Ean13,  "EAN13")]
    [InlineData(BarcodeFormat.Ean8,   "EAN8")]
    [InlineData(BarcodeFormat.UpcA,   "UPC")]
    [InlineData(BarcodeFormat.Code39, "CODE39")]
    [InlineData(BarcodeFormat.Itf14,  "ITF14")]
    public void FormatKeyShouldMapKnownFormatsToExpectedKeys(BarcodeFormat format, string expectedKey)
    {
        Barcode.FormatKey(format).Should().Be(expectedKey);
    }

    [Fact]
    public void FormatKeyShouldReturnCode128ForUnknownEnumValue()
    {
        var unknown = (BarcodeFormat)999;
        Barcode.FormatKey(unknown).Should().Be("CODE128");
    }

    [Fact]
    public void FormatKeyShouldReturnNonEmptyStringForEveryEnumValue()
    {
        foreach (var fmt in Enum.GetValues<BarcodeFormat>())
        {
            Barcode.FormatKey(fmt).Should().NotBeNullOrWhiteSpace();
        }
    }

    // ── Barcode parameter defaults ─────────────────────────────────────────────
    [Fact]
    public void BarcodeDefaultFormatShouldBeCode128()
    {
        var bc = new Barcode();
        bc.Format.Should().Be(BarcodeFormat.Code128);
    }

    [Fact]
    public void BarcodeDefaultHeightShouldBe80()
    {
        var bc = new Barcode();
        bc.Height.Should().Be(80);
    }

    [Fact]
    public void BarcodeDefaultBarWidthShouldBe2()
    {
        var bc = new Barcode();
        bc.BarWidth.Should().Be(2);
    }

    [Fact]
    public void BarcodeDefaultDisplayValueShouldBeTrue()
    {
        var bc = new Barcode();
        bc.DisplayValue.Should().BeTrue();
    }

    [Fact]
    public void BarcodeDefaultBackgroundColorShouldBeWhite()
    {
        var bc = new Barcode();
        bc.BackgroundColor.Should().Be("#ffffff");
    }

    [Fact]
    public void BarcodeDefaultLineColorShouldBeBlack()
    {
        var bc = new Barcode();
        bc.LineColor.Should().Be("#000000");
    }

    [Fact]
    public void BarcodeDefaultValueShouldBeNull()
    {
        var bc = new Barcode();
        bc.Value.Should().BeNull();
    }

    [Fact]
    public void BarcodeDefaultCssClassShouldBeNull()
    {
        var bc = new Barcode();
        bc.CssClass.Should().BeNull();
    }
}
