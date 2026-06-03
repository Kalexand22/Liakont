namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class QRCodeTests
{
    // ── QrEcLevel enum ─────────────────────────────────────────────────────────
    [Fact]
    public void QrEcLevelShouldHaveFourValues()
    {
        Enum.GetValues<QrEcLevel>().Should().HaveCount(4);
    }

    [Fact]
    public void QrEcLevelShouldContainAllExpectedMembers()
    {
        var values = Enum.GetValues<QrEcLevel>();
        values.Should().Contain(QrEcLevel.L);
        values.Should().Contain(QrEcLevel.M);
        values.Should().Contain(QrEcLevel.Q);
        values.Should().Contain(QrEcLevel.H);
    }

    // ── EcLevelKey mapping ─────────────────────────────────────────────────────
    [Theory]
    [InlineData(QrEcLevel.L, "L")]
    [InlineData(QrEcLevel.M, "M")]
    [InlineData(QrEcLevel.Q, "Q")]
    [InlineData(QrEcLevel.H, "H")]
    public void EcLevelKeyShouldMapKnownLevelsToExpectedKeys(QrEcLevel level, string expectedKey)
    {
        QRCode.EcLevelKey(level).Should().Be(expectedKey);
    }

    [Fact]
    public void EcLevelKeyShouldReturnMForUnknownEnumValue()
    {
        var unknown = (QrEcLevel)999;
        QRCode.EcLevelKey(unknown).Should().Be("M");
    }

    [Fact]
    public void EcLevelKeyShouldReturnNonEmptyStringForEveryEnumValue()
    {
        foreach (var level in Enum.GetValues<QrEcLevel>())
        {
            QRCode.EcLevelKey(level).Should().NotBeNullOrWhiteSpace();
        }
    }

    // ── QRCode parameter defaults ──────────────────────────────────────────────
    [Fact]
    public void QRCodeDefaultSizeShouldBe128()
    {
        var qr = new QRCode();
        qr.Size.Should().Be(128);
    }

    [Fact]
    public void QRCodeDefaultErrorCorrectionLevelShouldBeM()
    {
        var qr = new QRCode();
        qr.ErrorCorrectionLevel.Should().Be(QrEcLevel.M);
    }

    [Fact]
    public void QRCodeDefaultMarginShouldBe4()
    {
        var qr = new QRCode();
        qr.Margin.Should().Be(4);
    }

    [Fact]
    public void QRCodeDefaultValueShouldBeNull()
    {
        var qr = new QRCode();
        qr.Value.Should().BeNull();
    }

    [Fact]
    public void QRCodeDefaultCssClassShouldBeNull()
    {
        var qr = new QRCode();
        qr.CssClass.Should().BeNull();
    }
}
