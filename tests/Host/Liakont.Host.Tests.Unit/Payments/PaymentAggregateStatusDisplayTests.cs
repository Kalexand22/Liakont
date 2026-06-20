namespace Liakont.Host.Tests.Unit.Payments;

using FluentAssertions;
using Liakont.Host.Payments;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class PaymentAggregateStatusDisplayTests
{
    [Theory]
    [InlineData("Calculated", "À transmettre", Severity.Info)]
    [InlineData("Suspended", "Décision fiscale en attente", Severity.Warning)]
    [InlineData("NotRequired", "Non concerné (TVA sur les débits)", Severity.Neutral)]
    [InlineData("PendingCapability", "En attente (plateforme)", Severity.Warning)]
    [InlineData("SourceWithoutPayments", "Non transmis (source sans encaissements déclarés)", Severity.Warning)]
    public void Should_Map_Each_Status_To_Its_French_Label_And_Severity(string status, string expectedLabel, Severity expectedSeverity)
    {
        var (label, severity) = PaymentAggregateStatusDisplay.For(status);

        label.Should().Be(expectedLabel);
        severity.Should().Be(expectedSeverity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Fall_Back_To_A_Neutral_Placeholder_For_Empty_Status(string? status)
    {
        var (label, severity) = PaymentAggregateStatusDisplay.For(status);

        // Fonction totale : jamais d'exception, jamais de couleur trompeuse pour un statut absent.
        label.Should().Be("—");
        severity.Should().Be(Severity.Neutral);
    }

    [Fact]
    public void Should_Echo_An_Unknown_Status_Without_Throwing()
    {
        var (label, severity) = PaymentAggregateStatusDisplay.For("Martian");

        label.Should().Be("Martian");
        severity.Should().Be(Severity.Neutral);
    }
}
