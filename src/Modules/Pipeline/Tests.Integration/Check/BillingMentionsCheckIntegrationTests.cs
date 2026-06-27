namespace Liakont.Modules.Pipeline.Tests.Integration.Check;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// CHECK bout en bout de la garde des MENTIONS DE FACTURATION (BUG-26, F16 §3.5 / F12-A §3.4) sur une base
/// tenant PostgreSQL réelle : une facture B2B à émetteur FRANÇAIS (acheteur SIREN, montant dû positif) est
/// BLOQUÉE tant que le tenant n'a pas renseigné ses mentions légales FR (PMD/PMT/AAB) + des termes de
/// paiement / une échéance (BR-CO-25), et atteint <c>ReadyToSend</c> une fois les mentions paramétrées.
/// Émetteur rempli au read-time depuis le profil FR du harnais ; mentions injectées au read-time depuis le
/// tenant — aucun contenu inventé (CLAUDE.md n°2/3). Les deux scénarios sont dans un SEUL test pour piloter
/// de façon déterministe l'état partagé <c>billing_mentions</c> (un document distinct par scénario).
/// </summary>
public sealed class BillingMentionsCheckIntegrationTests : IClassFixture<PipelineCheckHarness>
{
    private readonly PipelineCheckHarness _harness;

    public BillingMentionsCheckIntegrationTests(PipelineCheckHarness harness) => _harness = harness;

    [Fact]
    public async Task French_B2b_Invoice_Is_Blocked_Without_Mentions_Then_Ready_With_Mentions()
    {
        // 1) Mentions ABSENTES → la facture B2B FR est bloquée, motif « mentions de facturation » consigné.
        await _harness.RemoveBillingMentionsAsync();

        var blockedId = Guid.NewGuid();
        var blockedRef = "no_ba=" + blockedId.ToString("N");
        var blockedPivot = CheckIntegrationFixtures.BuildFrenchB2bInvoice(blockedRef);
        await SeedAndStageAsync(blockedId, blockedRef, blockedPivot);

        await ConsumeAsync(blockedId, blockedRef, CheckIntegrationFixtures.PayloadHashOf(blockedPivot));

        (await _harness.GetDocumentStateAsync(blockedId)).Should().Be(
            "Blocked", "une facture B2B FR sans mentions de facturation est bloquée (« bloquer plutôt qu'envoyer faux »)");

        var blockedEvents = await _harness.GetEventsAsync(blockedId);
        blockedEvents.Should().Contain(
            e => e.Detail != null && e.Detail.Contains("mentions de facturation", StringComparison.Ordinal),
            "le motif de blocage des mentions est consigné dans la piste d'audit (INV-PIPELINE-011)");

        // 2) Mentions RENSEIGNÉES → la même facture (nouveau document) atteint ReadyToSend.
        await _harness.SeedBillingMentionsAsync();

        var readyId = Guid.NewGuid();
        var readyRef = "no_ba=" + readyId.ToString("N");
        var readyPivot = CheckIntegrationFixtures.BuildFrenchB2bInvoice(readyRef);
        await SeedAndStageAsync(readyId, readyRef, readyPivot);

        await ConsumeAsync(readyId, readyRef, CheckIntegrationFixtures.PayloadHashOf(readyPivot));

        (await _harness.GetDocumentStateAsync(readyId)).Should().Be(
            "ReadyToSend", "les mentions de facturation tenant levent le blocage — le document est prêt à l'envoi");

        var readyEvents = await _harness.GetEventsAsync(readyId);
        readyEvents.Should().NotContain(
            e => e.Detail != null && e.Detail.Contains("mentions de facturation", StringComparison.Ordinal),
            "une fois les mentions renseignées, la garde ne firent plus");
    }

    private async Task SeedAndStageAsync(Guid documentId, string sourceReference, PivotDocumentDto pivot)
    {
        var json = CanonicalJson.Serialize(pivot);
        var hash = PayloadHasher.ComputeHash(json);
        await _harness.SeedDetectedDocumentAsync(documentId, sourceReference, hash, pivot);
        await _harness.StagePayloadAsync(documentId, hash, json);
    }

    private async Task ConsumeAsync(Guid documentId, string sourceReference, string payloadHash)
    {
        var consumer = new DocumentReceivedConsumer(_harness.ScopeFactory, NullLogger<DocumentReceivedConsumer>.Instance);
        await consumer.HandleAsync(CheckIntegrationFixtures.Event(documentId, sourceReference, payloadHash));
    }
}
