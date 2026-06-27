namespace Liakont.Host.Tests.Unit.Components;

using FluentAssertions;
using Liakont.Host.Components;
using Xunit;

/// <summary>
/// Restitution lisible du snapshot de réponse PA (BUG-27 document / BUG-22 émission). Couvre les deux formes
/// réelles — enveloppe document <c>{ errors:[{Code,Message}], rawResponse }</c> et réponse PA brute
/// <c>{ http_status_code, message }</c> — plus les cas dégradés (non-JSON, vide, sans motif). Garantit « zéro
/// JSON brut » (F10 §1) et aucune exception (fonction totale).
/// </summary>
public sealed class PaResponseSnapshotFormatterTests
{
    [Fact]
    public void Formats_Document_Rejection_Errors_As_Code_And_Message_Lines()
    {
        const string snapshot = """{"state":"Rejected","errors":[{"Code":"BR-CO-25","Message":"Montant dû positif sans échéance."},{"Code":"BR-FR-05","Message":"Mention légale manquante."}],"rawResponse":"{}"}""";

        var lines = PaResponseSnapshotFormatter.Format(snapshot);

        lines.Should().HaveCount(2);
        lines[0].Should().Be("[BR-CO-25] Montant dû positif sans échéance.");
        lines[1].Should().Be("[BR-FR-05] Mention légale manquante.");
    }

    [Fact]
    public void Formats_A_Raw_Pa_Emission_Response_Message()
    {
        const string snapshot = """{"http_status_code":400,"message":"cannot add transaction at date 2024-01-03"}""";

        var lines = PaResponseSnapshotFormatter.Format(snapshot);

        lines.Should().ContainSingle().Which.Should().Be("cannot add transaction at date 2024-01-03");
    }

    [Fact]
    public void Falls_Back_To_Raw_Response_When_No_Structured_Errors()
    {
        const string snapshot = """{"state":"Rejected","errors":[],"rawResponse":"Service indisponible"}""";

        var lines = PaResponseSnapshotFormatter.Format(snapshot);

        lines.Should().ContainSingle().Which.Should().Be("Service indisponible");
    }

    [Fact]
    public void Never_Dumps_Nested_Json_From_Raw_Response()
    {
        // rawResponse qui est lui-même du JSON : on ne le dumpe pas brut (F10 §1) → aucune ligne.
        const string snapshot = """{"errors":[],"rawResponse":"{\"weird\":true}"}""";

        var lines = PaResponseSnapshotFormatter.Format(snapshot);

        lines.Should().BeEmpty();
    }

    [Fact]
    public void Returns_The_Raw_Text_When_The_Snapshot_Is_Not_Json()
    {
        var lines = PaResponseSnapshotFormatter.Format("503 Service Unavailable");

        lines.Should().ContainSingle().Which.Should().Be("503 Service Unavailable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_Empty_For_A_Missing_Snapshot(string? snapshot)
    {
        PaResponseSnapshotFormatter.Format(snapshot).Should().BeEmpty();
    }

    [Fact]
    public void Handles_An_Error_With_Only_A_Code()
    {
        const string snapshot = """{"errors":[{"Code":"PEPPOL-EN16931-R008"}]}""";

        var lines = PaResponseSnapshotFormatter.Format(snapshot);

        lines.Should().ContainSingle().Which.Should().Be("[PEPPOL-EN16931-R008]");
    }
}
