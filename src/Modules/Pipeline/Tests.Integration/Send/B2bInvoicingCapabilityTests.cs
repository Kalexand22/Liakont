namespace Liakont.Modules.Pipeline.Tests.Integration.Send;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Xunit;

/// <summary>
/// Filet de transmission B2B (décision Karl 2026-06-22 : « jamais une capacité d'une PA n'impacte le FLUX »).
/// Le FLUX (B2B vs B2C) vient du DOCUMENT — un acheteur identifié par un SIREN (assujetti adressable,
/// F07-F08 §A.4) = B2B — la capacité PA ne fait que GATER la transmission au bord : une facture B2B vers une
/// PA SANS <c>SupportsB2bInvoicing</c> est MAINTENUE (ReadyToSend), jamais dégradée en e-reporting B2C
/// (CLAUDE.md n°3/8). Une PA QUI déclare la capacité l'émet normalement. La garde PRIMAIRE est au CHECK
/// (<c>DocumentCheckEvaluator</c>) ; ce test couvre le filet d'envoi (parité avec la garde 389). Base ISOLÉE
/// par classe (un test laisse un document en <c>ReadyToSend</c>).
/// </summary>
public sealed class B2bInvoicingCapabilityTests : IAsyncLifetime
{
    /// <summary>SIREN acheteur FICTIF (CLAUDE.md n°7) — sa seule PRÉSENCE classe le document en B2B.</summary>
    private const string BuyerSiren = "552081317";

    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task B2b_Invoice_To_Pa_Without_B2bInvoicing_Capability_Is_Held_Never_Emitted()
    {
        // PA publiée mais ne déclarant PAS la facturation B2B : une facture B2B reste ReadyToSend (jamais
        // dégradée en e-reporting B2C, jamais émise faux — CLAUDE.md n°3/8).
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = WithoutB2bInvoicing() });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot(
            "send-b2b-nocap-" + documentId.ToString("N"),
            "NORMAL",
            new PivotPartyDto("Client Pro Fictif", siren: BuyerSiren));
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("ReadyToSend", "une facture B2B vers une PA sans capacité B2B est maintenue — jamais dégradée en e-reporting B2C.");
        _harness.PaClient.SentDocuments.Should().NotContain(d => d.SourceNumber == pivot.Number, "la capacité B2B n'est pas déclarée : aucun envoi.");
        _harness.PaClient.IssuedDocumentNumbers.Should().NotContain(pivot.Number);
    }

    [Fact]
    public async Task B2b_Invoice_To_Pa_With_B2bInvoicing_Capability_Is_Emitted()
    {
        // PA déclarant la facturation B2B (capacités V1 par défaut → SupportsB2bInvoicing = true) : la facture
        // B2B est émise normalement — la garde ne sur-bloque pas un flux légitime.
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot(
            "send-b2b-ok-" + documentId.ToString("N"),
            "NORMAL",
            new PivotPartyDto("Client Pro Fictif", siren: BuyerSiren));
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("Issued", "une facture B2B vers une PA déclarant la facturation B2B est émise normalement.");
        _harness.PaClient.IssuedDocumentNumbers.Should().Contain(pivot.Number);
    }

    [Fact]
    public async Task Identified_Recipient_Invoice_To_FacturX_Transmitter_Is_Not_Held_By_The_Guard()
    {
        // RÉGRESSION (revue 2026-06-22) : une PA de transport Factur-X (SupportsFacturXTransmission=true) SANS
        // routage PDP B2B (SupportsB2bInvoicing=false) — ex. Generique email/dépôt, Chorus Pro B2G — EST un canal
        // de transmission légitime : la plateforme produit le Factur-X EN 16931 (lignes BG-25), la PA le
        // transporte. La garde NE DOIT PAS maintenir un document à destinataire identifié (acheteur SIREN, B2B ou
        // B2G) dans ce cas — sinon Chorus Pro (dépôt B2G) et Generique sont cassés pour tout acheteur à SIREN.
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = FacturXTransmitterOnly() });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot(
            "send-b2b-facturx-" + documentId.ToString("N"),
            "NORMAL",
            new PivotPartyDto("Acheteur Public Fictif", siren: BuyerSiren));
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        // La garde ne tient PAS le document : il QUITTE ReadyToSend (transmission engagée — la génération réelle
        // du Factur-X est câblée au Host, hors de ce harnais). AVANT le correctif (garde gatant le seul
        // SupportsB2bInvoicing), il restait ReadyToSend à tort : ce test aurait alors échoué.
        (await _harness.GetDocumentStateAsync(documentId))
            .Should().NotBe("ReadyToSend", "un transporteur Factur-X est un canal légitime — la garde ne maintient pas le document à destinataire identifié.");
    }

    /// <summary>Capacités d'une PA publiée générale MAIS sans la facturation B2B (le reste = défaut V1).</summary>
    private static PaCapabilities WithoutB2bInvoicing() =>
        new FakePaClientOptions().Capabilities with { SupportsB2bInvoicing = false };

    /// <summary>PA de niveau « Essentiel » : transporte un Factur-X (SupportsFacturXTransmission) SANS routage PDP B2B.</summary>
    private static PaCapabilities FacturXTransmitterOnly() =>
        new FakePaClientOptions().Capabilities with { SupportsB2bInvoicing = false, SupportsFacturXTransmission = true };
}
