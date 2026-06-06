namespace Liakont.Modules.Pipeline.Tests.Integration.EndToEnd;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Xunit;

/// <summary>
/// E2E du pipeline (PIP01d) sur DEUX tenants, chacun sur sa PROPRE base PostgreSQL réelle (database-per-tenant) :
/// un document pivot de contrat v1 traverse <c>ingestion → CHECK → SEND (Fake) → SYNC → archive WORM</c>, et
/// l'ISOLATION tenant est vérifiée (le document d'un tenant n'existe jamais dans la base de l'autre).
/// </summary>
/// <remarks>
/// Le document est construit par <see cref="CheckIntegrationFixtures.BuildPivot"/> — le pivot contrat-v1 partagé
/// par les suites CHECK/SEND (même ligne « Adjudication lot 7 », régime « NORMAL », totaux 120/24/144). Les golden
/// JSON <c>tests/fixtures/contrat-v1/</c> portent des SIREN émetteur PLACEHOLDER non conformes à la clé de Luhn
/// (ils ciblent la suite de round-trip canonique <c>PivotCanonicalJsonReaderTests</c>, pas la validation) : la
/// règle d'identité émetteur (VAL02, SupplierIdentityRule) les bloquerait avant l'envoi. Ce pivot, sans SIREN
/// émetteur porté par le document et avec un profil tenant au SIREN valide, est donc le document contrat-v1
/// VALIDABLE équivalent — il exerce la chaîne complète jusqu'à l'archive.
/// </remarks>
public sealed class PipelineEndToEndTests : IAsyncLifetime
{
    private readonly PipelineE2ETenant _tenantA = new("acme");
    private readonly PipelineE2ETenant _tenantB = new("globex");

    public async Task InitializeAsync() =>
        await Task.WhenAll(_tenantA.InitializeAsync(), _tenantB.InitializeAsync());

    public async Task DisposeAsync()
    {
        await _tenantA.DisposeAsync();
        await _tenantB.DisposeAsync();
    }

    [Fact]
    public async Task Document_Flows_Ingestion_Check_Send_Sync_Archive_And_Stays_Isolated_Across_Tenants()
    {
        var pivot = CheckIntegrationFixtures.BuildPivot("no_ba=e2e-pip01d", regimeCode: "NORMAL");
        var payloadHash = PayloadHasher.ComputeHash(CanonicalJson.Serialize(pivot));

        var documentA = await RunChainAsync(_tenantA, pivot, payloadHash);
        var documentB = await RunChainAsync(_tenantB, pivot, payloadHash);

        // Isolation database-per-tenant : un même document source produit DEUX documents distincts, chacun
        // confiné à la base de son tenant (jamais de fuite cross-tenant — CLAUDE.md n°9/17).
        documentA.Should().NotBe(documentB, "chaque tenant attribue son propre identifiant de document.");
        (await _tenantB.DocumentExistsAsync(documentA)).Should().BeFalse("le document du tenant A n'apparaît pas dans la base du tenant B.");
        (await _tenantA.DocumentExistsAsync(documentB)).Should().BeFalse("le document du tenant B n'apparaît pas dans la base du tenant A.");
    }

    private static async Task<Guid> RunChainAsync(PipelineE2ETenant tenant, PivotDocumentDto pivot, string payloadHash)
    {
        // 1) Ingestion réelle : réception + staging + Detected + événement DocumentReceived.
        (await tenant.IngestAsync(pivot)).Should().Be(DocumentPushStatus.Accepted);

        var documentId = await tenant.ResolveDocumentIdAsync(pivot.SourceReference, payloadHash);
        documentId.Should().NotBeNull("l'ingestion a rangé le document en Detected.");

        // 2) CHECK : mapping (table validée) + validation → ReadyToSend.
        await tenant.RunCheckAsync(documentId!.Value, pivot.SourceReference, payloadHash);
        (await tenant.GetDocumentStateAsync(documentId.Value))
            .Should().Be("ReadyToSend", "régime NORMAL mappé sur une table validée + validation OK.");

        // 3) SEND : émission (Fake) → archive WORM → purge du staging subordonnée au WORM.
        await tenant.RunSendAsync();
        (await tenant.GetDocumentStateAsync(documentId.Value)).Should().Be("Issued");
        (await tenant.IsStagedAsync(documentId.Value, payloadHash))
            .Should().BeFalse("le staging est purgé une fois le paquet WORM effectivement présent (ADR-0014 §4).");

        // 4) SYNC : facture PA générée + tax report du document → addenda chaînés.
        await tenant.RunSyncAsync();
        (await tenant.ArchiveEntryCountAsync(documentId.Value))
            .Should().Be(3, "paquet WORM initial (SEND) + facture PA + tax report (SYNC) en addenda chaînés.");

        return documentId.Value;
    }
}
