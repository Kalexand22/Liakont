namespace Liakont.Host.Tests.Unit.Components;

using FluentAssertions;
using Liakont.Host.Components;
using Xunit;

public sealed class DocumentEventDisplayTests
{
    [Theory]
    [InlineData("DocumentDetected", "Détecté")]
    [InlineData("DocumentBlocked", "Bloqué")]
    [InlineData("DocumentReadyToSend", "Prêt à envoyer")]
    [InlineData("DocumentSending", "Envoi engagé")]
    [InlineData("DocumentIssued", "Émis")]
    [InlineData("DocumentRejectedByPa", "Rejeté par la Plateforme Agréée")]
    [InlineData("DocumentTechnicalError", "Erreur technique")]
    [InlineData("DocumentSuperseded", "Remplacé")]
    [InlineData("DocumentManuallyHandled", "Traité manuellement")]
    [InlineData("DocumentSourceAlteredAfterIssue", "Source altérée après émission")]
    [InlineData("DocumentReconciledAuto", "PDF rapproché automatiquement")]
    [InlineData("DocumentReconciledManual", "PDF rapproché manuellement")]
    [InlineData("DocumentBuyerConfirmedB2C", "Acheteur confirmé particulier (B2C)")]
    public void For_Should_Map_Known_Event_Types_To_French(string raw, string expected)
    {
        DocumentEventDisplay.For(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void For_Should_Return_Dash_For_Empty(string? raw)
    {
        DocumentEventDisplay.For(raw).Should().Be("—");
    }

    [Theory]
    [InlineData("SomethingUnknown")]
    [InlineData("DocumentFoo")]
    public void For_Should_Fall_Back_To_The_Raw_Value_For_Unknown_Types(string raw)
    {
        DocumentEventDisplay.For(raw).Should().Be(raw);
    }
}
