namespace Liakont.Host.Tests.Unit.B2cReporting;

using FluentAssertions;
using Liakont.Host.B2cReporting;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class B2cMarginEmissionStatusDisplayTests
{
    [Theory]
    [InlineData("Issued", "Émis", Severity.Success)]
    [InlineData("Pending", "Transmission engagée", Severity.Warning)]
    [InlineData("RejectedByPa", "Rejeté par la plateforme", Severity.Error)]
    [InlineData("Technical", "Échec technique", Severity.Error)]
    public void Should_Map_Each_Status_To_Its_French_Label_And_Severity(string status, string expectedLabel, Severity expectedSeverity)
    {
        var (label, severity) = B2cMarginEmissionStatusDisplay.For(status);

        label.Should().Be(expectedLabel);
        severity.Should().Be(expectedSeverity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Fall_Back_To_A_Neutral_Placeholder_For_Empty_Status(string? status)
    {
        var (label, severity) = B2cMarginEmissionStatusDisplay.For(status);

        // Fonction totale : jamais d'exception, jamais de couleur trompeuse pour un statut absent.
        label.Should().Be("—");
        severity.Should().Be(Severity.Neutral);
    }

    [Fact]
    public void Should_Echo_An_Unknown_Status_Without_Throwing()
    {
        var (label, severity) = B2cMarginEmissionStatusDisplay.For("Martian");

        label.Should().Be("Martian");
        severity.Should().Be(Severity.Neutral);
    }
}
