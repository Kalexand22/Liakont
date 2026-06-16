namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Documents.Contracts.Deduplication;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure.Deduplication;
using Npgsql;
using Xunit;

/// <summary>
/// Re-clé anti-doublon F06 §4 pour l'autofacturation (BT-3 = 389) — item MND06, ADR-0025 §6 (F06 §4 amendé),
/// F15 §3.2/§3.3, INV-BT1-5/INV-BT1-6. En 389 la clé fonctionnelle bascule de
/// <c>(supplier_siren, document_number)</c> vers <c>(mandant_id, document_number = BT-1 fiscal alloué)</c> :
/// le « supplier » fiscal est le mandant. Deux 389 de même numéro mais mandants différents NE sont PAS doublons
/// (séquences distinctes) ; même mandant + ré-extraction = doublon. La bascule est atomique côté base (index
/// d'unicité PARTIEL sur l'état <c>Issued</c>, V010) sans casser le remplacement F06 §4.3. Exercé sur PostgreSQL
/// réel (Testcontainers) ; chaque test isole ses clés (mandant/numéro uniques) dans la base partagée.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class SelfBilled389AntiDuplicateIntegrationTests
{
    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public SelfBilled389AntiDuplicateIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ── INV-BT1-5 : cartésien anti-doublon 389 (mandants × ré-extraction) ───────────────────────────────

    [Fact]
    public async Task Two_389_Of_Same_Number_But_Different_Mandants_Are_Not_Duplicates()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("BT1");
        var mandantA = Guid.NewGuid();
        var mandantB = Guid.NewGuid();

        // Un 389 du mandant A déjà émis sous ce BT-1. Le mandant B alloue indépendamment (séquence par mandant) :
        // un même numéro chez B n'est PAS un doublon de celui de A (la clé inclut mandant_id — F06 §4 amendé).
        await SeedAsync(harness, DocumentTestData.Reconstituted(
            DocumentState.Issued, documentNumber: number, payloadHash: Unique("h"), mandantId: mandantA));

        var result = await Check389Async(harness, mandantB, number, Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.Send);
        result.MaySend.Should().BeTrue();
    }

    [Fact]
    public async Task Same_Mandant_Re_Extraction_Of_An_Issued_389_Is_Blocked()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("BT1");
        var mandant = Guid.NewGuid();

        // 389 déjà émis (mandant M, BT-1 N). Une ré-extraction relit le MÊME BT-1 (allocation idempotente, MND05) :
        // la clé (mandant_id, document_number) rapproche l'antécédent Issued → doublon bloqué (F06 §4.2).
        var issued = DocumentTestData.Reconstituted(
            DocumentState.Issued, documentNumber: number, payloadHash: Unique("h"), mandantId: mandant);
        await SeedAsync(harness, issued);

        var result = await Check389Async(harness, mandant, number, Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.BlockedAlreadyIssued);
        result.RelatedDocumentId.Should().Be(issued.Id);
        result.MaySend.Should().BeFalse();
    }

    [Fact]
    public async Task Same_Mandant_Rejected_389_Allows_Resend_Superseding_The_Old()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("BT1");
        var mandant = Guid.NewGuid();

        // Parité avec F06 §4.3 sur la clé 389 : un 389 rejeté par la PA autorise le renvoi en désignant l'ancien
        // à superséder. Le remplacement reste possible (l'index d'unicité 389 est PARTIEL sur Issued — V010).
        var rejected = DocumentTestData.Reconstituted(
            DocumentState.RejectedByPa, documentNumber: number, payloadHash: Unique("h"), mandantId: mandant);
        await SeedAsync(harness, rejected);

        var result = await Check389Async(harness, mandant, number, Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.ResendSupersedingRejected);
        result.RelatedDocumentId.Should().Be(rejected.Id);
        result.MaySend.Should().BeTrue();
    }

    [Fact]
    public async Task A_389_Is_Never_A_Duplicate_Of_Itself()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("BT1");
        var mandant = Guid.NewGuid();
        var hash = Unique("h");
        var self = DocumentTestData.Reconstituted(
            DocumentState.Issued, documentNumber: number, payloadHash: hash, mandantId: mandant);
        await SeedAsync(harness, self);

        // Le candidat EST le 389 émis (même id) : il ne se bloque pas lui-même (id <> @CandidateId).
        var check = new PostgresDuplicateDocumentCheck(harness.ConnectionFactory);
        var result = await check.EvaluateAsync(new DuplicateCheckRequest
        {
            DocumentId = self.Id,
            MandantId = mandant,
            DocumentNumber = number,
            PayloadHash = hash,
        });

        result.Decision.Should().Be(DuplicateCheckDecision.Send);
    }

    [Fact]
    public async Task A_New_389_Of_A_Fresh_Mandant_Is_Authorized_To_Send()
    {
        var harness = new DocumentsHarness(_fixture);

        var result = await Check389Async(harness, Guid.NewGuid(), Unique("BT1"), Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.Send);
        result.RelatedDocumentId.Should().BeNull();
        result.MaySend.Should().BeTrue();
    }

    // ── Non-régression non-389 : la clé historique (supplier_siren, document_number) reste appliquée ─────

    [Fact]
    public async Task A_Non_389_Issued_With_The_Same_Number_Does_Not_Block_A_389_Of_A_Mandant()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("BT1");
        var mandant = Guid.NewGuid();

        // Antécédent NON-389 (mandant_id NULL) émis sous le même numéro mais avec un SIREN fournisseur : il ne
        // partage PAS la clé 389 (mandant_id) → il ne rapproche pas le 389. Les deux clés cohabitent sans fuite.
        await SeedAsync(harness, DocumentTestData.Reconstituted(
            DocumentState.Issued, documentNumber: number, supplierSiren: Unique("siren"), payloadHash: Unique("h")));

        var result = await Check389Async(harness, mandant, number, Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.Send);
    }

    [Fact]
    public async Task A_389_Issued_Does_Not_Block_A_Non_389_Of_The_Same_Number()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("BT1");

        // Antécédent 389 (mandant_id renseigné) émis sous ce numéro : il ne doit pas bloquer un document NON-389
        // de même (supplier_siren, document_number) — la clé historique ignore mandant_id (aucune régression).
        await SeedAsync(harness, DocumentTestData.Reconstituted(
            DocumentState.Issued, documentNumber: number, payloadHash: Unique("h"), mandantId: Guid.NewGuid()));

        var siren = Unique("siren");
        var result = await CheckNon389Async(harness, siren, number, Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.Send);
    }

    // ── INV-BT1-6 : index d'unicité 389 ATOMIQUE côté base (jamais neutralisation applicative du SIREN) ──

    [Fact]
    public async Task Unique_Index_Forbids_A_Second_Issued_389_With_Same_Mandant_And_Number()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("BT1");
        var mandant = Guid.NewGuid();

        await SeedAsync(harness, DocumentTestData.Reconstituted(
            DocumentState.Issued, documentNumber: number, payloadHash: Unique("h"), mandantId: mandant));

        // Backstop atomique : deux 389 ÉMIS de même (mandant_id, document_number = BT-1) sont impossibles en base
        // (ferme la course où deux ré-extractions concurrentes franchiraient toutes deux la garde applicative).
        var duplicate = DocumentTestData.Reconstituted(
            DocumentState.Issued, documentNumber: number, payloadHash: Unique("h"), mandantId: mandant);

        var act = async () =>
        {
            await using var uow = await harness.UowFactory.BeginAsync();
            await uow.UpsertDocumentAsync(duplicate);
            await uow.CommitAsync();
        };

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Unique_Index_Does_Not_Constrain_Non_389_Documents_Of_The_Same_Number()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("F");

        // Deux NON-389 (mandant_id NULL) émis sous le même numéro restent possibles : l'index 389 est PARTIEL
        // (WHERE mandant_id IS NOT NULL) → le remplacement F06 §4.3 des non-389 n'est pas cassé (INV-DOCUMENTS-006).
        await SeedAsync(harness, DocumentTestData.Reconstituted(
            DocumentState.Issued, documentNumber: number, supplierSiren: Unique("siren"), payloadHash: Unique("h")));

        var act = async () =>
        {
            await using var uow = await harness.UowFactory.BeginAsync();
            await uow.UpsertDocumentAsync(DocumentTestData.Reconstituted(
                DocumentState.Issued, documentNumber: number, supplierSiren: Unique("siren"), payloadHash: Unique("h")));
            await uow.CommitAsync();
        };

        await act.Should().NotThrowAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<DuplicateCheckResult> Check389Async(
        DocumentsHarness harness, Guid mandantId, string documentNumber, string payloadHash)
    {
        var check = new PostgresDuplicateDocumentCheck(harness.ConnectionFactory);
        return await check.EvaluateAsync(new DuplicateCheckRequest
        {
            DocumentId = Guid.NewGuid(),
            MandantId = mandantId,
            DocumentNumber = documentNumber,
            PayloadHash = payloadHash,
        });
    }

    private static async Task<DuplicateCheckResult> CheckNon389Async(
        DocumentsHarness harness, string supplierSiren, string documentNumber, string payloadHash)
    {
        var check = new PostgresDuplicateDocumentCheck(harness.ConnectionFactory);
        return await check.EvaluateAsync(new DuplicateCheckRequest
        {
            DocumentId = Guid.NewGuid(),
            SupplierSiren = supplierSiren,
            DocumentNumber = documentNumber,
            PayloadHash = payloadHash,
        });
    }

    private static async Task SeedAsync(DocumentsHarness harness, Document document)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.UpsertDocumentAsync(document);
        await uow.CommitAsync();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
