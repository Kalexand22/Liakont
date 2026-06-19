namespace Liakont.Modules.Pipeline.Tests.Integration.Send;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Xunit;

/// <summary>
/// SEND (PIP01c) de l'autofacturation sous mandat (MND07, F15 §1.2) bout en bout vers le plug-in PA factice :
/// un document <c>IsSelfBilled</c>, accepté (MND02/03) et dont le BT-1 fiscal est alloué (MND05), est transmis
/// en type BT-3 = <b>389</b>, vendeur fiscal = le mandant (BT-30 SIREN / BT-31 n° TVA = le <c>Supplier</c> du
/// pivot), BT-1 = le numéro fiscal alloué (≠ numéro source). Et : une PA sans capacité 389 ou un BT-1 fiscal
/// non alloué ⇒ document MAINTENU (jamais émis faux ni dégradé en facture standard — CLAUDE.md n°3/8).
/// Base tenant PostgreSQL réelle (Testcontainers), archive WORM et staging réels.
/// </summary>
public sealed class SelfBilledSendIntegrationTests : IClassFixture<PipelineSendHarness>
{
    private const string MandantSiren = "552081317";
    private const string MandantVatNumber = "FR89552081317";

    private readonly PipelineSendHarness _harness;

    public SelfBilledSendIntegrationTests(PipelineSendHarness harness) => _harness = harness;

    [Fact]
    public async Task SelfBilled_Accepted_And_Allocated_Is_Emitted_As_389_With_Mandant_Seller_And_Allocated_Bt1()
    {
        await _harness.UsePublishedFakeAsync(); // capacités V1 par défaut → SupportsSelfBilling = true
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var allocatedBt1 = "ARM-A-" + documentId.ToString("N")[..6];
        var pivot = CheckIntegrationFixtures.BuildSelfBilledPivot(
            "selfbilled-ok-" + documentId.ToString("N"), MandantSiren, MandantVatNumber);
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        _harness.SeedSelfBilledAcceptance(documentId, allocatedBt1);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Issued");

        var sent = _harness.PaClient.SentDocuments.Should()
            .ContainSingle(d => d.SourceNumber == pivot.Number).Subject;
        sent.DocumentTypeCode.Should().Be("389", "autofacturation sous mandat (BT-3 = 389, F15 §1.2)");
        sent.FiscalNumber.Should().Be(allocatedBt1, "BT-1 = numéro fiscal alloué par mandant (MND05), pas le numéro source");
        sent.IsSelfBilled.Should().BeTrue();
        sent.SellerSiren.Should().Be(MandantSiren, "vendeur fiscal = le mandant (BT-30, ADR-0025 §7)");
        sent.SellerVatNumber.Should().Be(MandantVatNumber, "BT-31 du mandant (F15 §2.2)");
        _harness.PaClient.IssuedDocumentNumbers.Should().Contain(allocatedBt1);
        _harness.PaClient.IssuedDocumentNumbers.Should().NotContain(pivot.Number, "la PA émet/déduplique sur le BT-1 fiscal, pas le numéro source");
    }

    [Fact]
    public async Task SelfBilled_To_Pa_Without_SelfBilling_Capability_Is_Held_Never_Emitted()
    {
        var caps = new FakePaClientOptions().Capabilities with { SupportsSelfBilling = false };
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = caps });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildSelfBilledPivot(
            "selfbilled-incapable-" + documentId.ToString("N"), MandantSiren, MandantVatNumber);
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        _harness.SeedSelfBilledAcceptance(documentId, "ARM-A-INCAP");

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("ReadyToSend", "PA incapable de 389 ⇒ maintenu, jamais émis");
        _harness.PaClient.SentDocuments.Should().NotContain(d => d.SourceNumber == pivot.Number, "jamais transmis (ni en 389 ni dégradé en 380)");
        _harness.PaClient.IssuedDocumentNumbers.Should().BeEmpty();
    }

    [Fact]
    public async Task SelfBilled_Without_Allocated_Bt1_Is_Held_Never_Emitted_With_Source_Number()
    {
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildSelfBilledPivot(
            "selfbilled-noalloc-" + documentId.ToString("N"), MandantSiren, MandantVatNumber);
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        _harness.SeedSelfBilledAcceptance(documentId, allocatedNumber: null);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("ReadyToSend", "BT-1 fiscal non alloué ⇒ maintenu (fail-closed)");
        _harness.PaClient.SentDocuments.Should().NotContain(d => d.SourceNumber == pivot.Number, "jamais émis avec le numéro source en place du BT-1 fiscal (INV-BT1-1)");
        _harness.PaClient.IssuedDocumentNumbers.Should().NotContain(pivot.Number);
    }

    [Fact]
    public async Task SelfBilled_Not_Accepted_At_Send_Is_Held_Never_Emitted()
    {
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var allocatedBt1 = "ARM-A-" + documentId.ToString("N")[..6];
        var pivot = CheckIntegrationFixtures.BuildSelfBilledPivot(
            "selfbilled-notaccepted-" + documentId.ToString("N"), MandantSiren, MandantVatNumber);
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        _harness.SeedSelfBilledAcceptance(documentId, allocatedBt1, isAccepted: false, state: "Contested");

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("ReadyToSend", "acceptation non acquise ⇒ maintenu (fail-closed)");
        _harness.PaClient.SentDocuments.Should().NotContain(d => d.SourceNumber == pivot.Number, "jamais transmis sans acceptation (INV-ACCEPT-2)");
        _harness.PaClient.IssuedDocumentNumbers.Should().NotContain(allocatedBt1);
    }

    [Fact]
    public async Task SelfBilled_When_Capable_Pa_Refuses_The_389_Is_Rejected_Never_Looping_In_TechnicalError()
    {
        // Filet anti-boucle (MND07, revue round 2) : une PA INCOHÉRENTE déclare SupportsSelfBilling=true mais
        // son sérialiseur refuse le 389 (résultat typé). Le SEND émet (capacité déclarée) → le pipeline doit
        // traiter le refus comme un REJET DÉFINITIF, jamais retomber en TechnicalError re-tentable (boucle).
        var caps = new FakePaClientOptions().Capabilities; // SupportsSelfBilling = true
        await _harness.UsePublishedFakeAsync(new FakePaClientOptions { Capabilities = caps, RefuseSelfBillingProjection = true });
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var allocatedBt1 = "ARM-A-" + documentId.ToString("N")[..6];
        var pivot = CheckIntegrationFixtures.BuildSelfBilledPivot(
            "selfbilled-refused-" + documentId.ToString("N"), MandantSiren, MandantVatNumber);
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        _harness.SeedSelfBilledAcceptance(documentId, allocatedBt1);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should()
            .Be("RejectedByPa", "une PA incohérente (déclare 389 mais le refuse) → rejet définitif, jamais TechnicalError en boucle");
    }
}
