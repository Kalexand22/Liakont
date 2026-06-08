namespace Liakont.Host.Tests.Unit.Components;

using FluentAssertions;
using Liakont.Host.Components;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class DocumentStateDisplayTests
{
    [Theory]
    [InlineData("Detected", "À envoyer", Severity.Neutral)]
    [InlineData("ReadyToSend", "Prêt à envoyer", Severity.Info)]
    [InlineData("Sending", "En cours", Severity.Info)]
    [InlineData("Blocked", "Bloqué", Severity.Warning)]
    [InlineData("TechnicalError", "Erreur technique", Severity.Error)]
    [InlineData("RejectedByPa", "Rejeté", Severity.Error)]
    [InlineData("Issued", "Émis", Severity.Success)]
    [InlineData("Superseded", "Remplacé", Severity.Neutral)]
    [InlineData("ManuallyHandled", "Traité manuellement", Severity.Neutral)]
    public void For_Should_Map_Each_State_To_French_Label_And_Severity(string state, string expectedFragment, Severity expectedSeverity)
    {
        var (label, severity) = DocumentStateDisplay.For(state);

        label.Should().Contain(expectedFragment);
        severity.Should().Be(expectedSeverity);
    }

    [Fact]
    public void For_Should_Fallback_To_Raw_State_For_Unknown_State()
    {
        var (label, severity) = DocumentStateDisplay.For("SomethingNew");

        label.Should().Be("SomethingNew");
        severity.Should().Be(Severity.Neutral);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void For_Should_Render_Placeholder_For_Empty_State(string? state)
    {
        var (label, severity) = DocumentStateDisplay.For(state);

        label.Should().Be("—");
        severity.Should().Be(Severity.Neutral);
    }

    [Fact]
    public void CanonicalOrder_Should_Cover_The_Key_States()
    {
        DocumentStateDisplay.CanonicalOrder.Should().HaveCount(9);
        DocumentStateDisplay.CanonicalOrder.Should().Contain(["Detected", "Blocked", "Issued", "RejectedByPa"]);
    }

    [Fact]
    public void Every_Canonical_State_Should_Have_A_Non_Empty_Mapped_Label()
    {
        foreach (var state in DocumentStateDisplay.CanonicalOrder)
        {
            var (label, _) = DocumentStateDisplay.For(state);
            label.Should().NotBeNullOrWhiteSpace();
            label.Should().NotBe(state, "un état canonique doit avoir un libellé opérateur français, pas son nom brut");
        }
    }
}
