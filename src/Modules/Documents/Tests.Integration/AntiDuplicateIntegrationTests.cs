namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.Deduplication;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure.Deduplication;
using Liakont.Modules.Documents.Infrastructure.Lookups;
using Liakont.Modules.Validation.Contracts.CreditNotes;
using Xunit;

/// <summary>
/// Anti-doublon F06 §4 + ports d'unicité (VAL03) et d'avoirs (VAL04) implémentés par TRK03, exercés sur
/// PostgreSQL réel (Testcontainers). La base unique = la base DU TENANT. Chaque test utilise des clés
/// (SIREN/numéro/empreinte) UNIQUES pour s'isoler dans la base partagée par la collection.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class AntiDuplicateIntegrationTests
{
    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public AntiDuplicateIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ── F06 §4 : anti-doublon (les quatre règles, une par une) ──────────────────────────────────────────

    [Fact]
    public async Task Rule_42_Document_Already_Issued_Is_A_Duplicate_And_Is_Blocked()
    {
        var harness = new DocumentsHarness(_fixture);
        var siren = Unique("siren");
        var number = Unique("F");
        var issued = DocumentTestData.Reconstituted(DocumentState.Issued, supplierSiren: siren, documentNumber: number, payloadHash: Unique("h"));
        await SeedAsync(harness, issued);

        var result = await CheckAsync(harness, siren, number, Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.BlockedAlreadyIssued);
        result.RelatedDocumentId.Should().Be(issued.Id);
        result.MaySend.Should().BeFalse();
    }

    [Fact]
    public async Task Rule_43_Resend_After_Rejection_Is_Allowed_And_Names_The_Document_To_Supersede()
    {
        var harness = new DocumentsHarness(_fixture);
        var siren = Unique("siren");
        var number = Unique("F");
        var rejected = DocumentTestData.Reconstituted(DocumentState.RejectedByPa, supplierSiren: siren, documentNumber: number, payloadHash: Unique("h"));
        await SeedAsync(harness, rejected);

        var result = await CheckAsync(harness, siren, number, Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.ResendSupersedingRejected);
        result.RelatedDocumentId.Should().Be(rejected.Id, "l'ancien rejeté doit passer Superseded (F06 §4.3).");
        result.MaySend.Should().BeTrue();
    }

    [Fact]
    public async Task Rule_44_Same_Payload_Hash_As_An_Issued_Document_Is_A_Strict_Duplicate()
    {
        var harness = new DocumentsHarness(_fixture);
        var hash = Unique("h");

        // Empreinte jumelle déjà émise, mais SOUS UNE AUTRE clé fonctionnelle (siren/numéro différents) :
        // c'est le garde-fou 4.4 (ré-extraction involontaire d'un contenu déjà émis), pas la règle 4.2.
        var issued = DocumentTestData.Reconstituted(DocumentState.Issued, supplierSiren: Unique("siren"), documentNumber: Unique("F"), payloadHash: hash);
        await SeedAsync(harness, issued);

        var result = await CheckAsync(harness, Unique("siren"), Unique("F"), hash);

        result.Decision.Should().Be(DuplicateCheckDecision.BlockedStrictDuplicate);
        result.RelatedDocumentId.Should().Be(issued.Id);
        result.MaySend.Should().BeFalse();
    }

    [Fact]
    public async Task Rule_45_A_New_Document_Is_Authorized_To_Send()
    {
        var harness = new DocumentsHarness(_fixture);

        var result = await CheckAsync(harness, Unique("siren"), Unique("F"), Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.Send);
        result.RelatedDocumentId.Should().BeNull();
        result.MaySend.Should().BeTrue();
    }

    [Fact]
    public async Task A_Document_Is_Never_A_Duplicate_Of_Itself()
    {
        var harness = new DocumentsHarness(_fixture);
        var siren = Unique("siren");
        var number = Unique("F");
        var hash = Unique("h");
        var self = DocumentTestData.Reconstituted(DocumentState.Issued, supplierSiren: siren, documentNumber: number, payloadHash: hash);
        await SeedAsync(harness, self);

        // Le candidat EST le document émis (même id) : il ne doit pas se bloquer lui-même.
        var check = new PostgresDuplicateDocumentCheck(harness.ConnectionFactory);
        var result = await check.EvaluateAsync(new DuplicateCheckRequest
        {
            DocumentId = self.Id,
            SupplierSiren = siren,
            DocumentNumber = number,
            PayloadHash = hash,
        });

        result.Decision.Should().Be(DuplicateCheckDecision.Send);
    }

    [Fact]
    public async Task Without_A_Supplier_Siren_The_Functional_Key_Does_Not_Match_A_Prior()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("F");

        // Antécédent émis SANS SIREN, même numéro : sans SIREN la clé fonctionnelle est incomplète, on ne
        // peut affirmer « même fournisseur » → pas de blocage par 4.2/4.3 (le garde-fou d'empreinte reste).
        var issuedNoSiren = DocumentTestData.Reconstituted(DocumentState.Issued, supplierSiren: null, documentNumber: number, payloadHash: Unique("h"));
        await SeedAsync(harness, issuedNoSiren);

        var result = await CheckAsync(harness, supplierSiren: null, documentNumber: number, payloadHash: Unique("h"));

        result.Decision.Should().Be(DuplicateCheckDecision.Send);
    }

    // ── VAL03 : IIssuedDocumentLookup ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IssuedDocumentLookup_Reports_An_Issued_Number_As_Already_Issued()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = Unique("F");
        await SeedAsync(harness, DocumentTestData.Reconstituted(DocumentState.Issued, documentNumber: number, payloadHash: Unique("h")));

        var lookup = new IssuedDocumentLookup(harness.ConnectionFactory);

        (await lookup.IsAlreadyIssuedAsync(Guid.NewGuid(), number)).Should().BeTrue();
    }

    [Fact]
    public async Task IssuedDocumentLookup_Does_Not_Report_A_Non_Issued_Or_Unknown_Number()
    {
        var harness = new DocumentsHarness(_fixture);
        var blockedNumber = Unique("F");
        await SeedAsync(harness, DocumentTestData.Reconstituted(DocumentState.Blocked, documentNumber: blockedNumber, payloadHash: Unique("h")));

        var lookup = new IssuedDocumentLookup(harness.ConnectionFactory);

        (await lookup.IsAlreadyIssuedAsync(Guid.NewGuid(), blockedNumber)).Should().BeFalse("le document existe mais n'est pas émis.");
        (await lookup.IsAlreadyIssuedAsync(Guid.NewGuid(), Unique("F"))).Should().BeFalse("numéro inconnu.");
    }

    // ── VAL04 : IIssuedInvoiceLookup (avoirs) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IssuedInvoiceLookup_Maps_Original_Invoice_State()
    {
        var harness = new DocumentsHarness(_fixture);
        var lookup = new IssuedInvoiceLookup(harness.ConnectionFactory);

        var issuedNumber = Unique("FAC");
        await SeedAsync(harness, DocumentTestData.Reconstituted(DocumentState.Issued, documentNumber: issuedNumber, payloadHash: Unique("h")));

        // Document connu pour ce numéro mais NON émis. On évite l'état ReadyToSend : un autre test
        // (GetByState_Is_Paginated_And_Filtered) compte EXACTEMENT les ReadyToSend du tenant dans la base
        // partagée par la collection — y ajouter un document fausserait son décompte (isolation par état).
        var notIssuedNumber = Unique("FAC");
        await SeedAsync(harness, DocumentTestData.Reconstituted(DocumentState.Blocked, documentNumber: notIssuedNumber, payloadHash: Unique("h")));

        (await lookup.FindOriginalStatusAsync(Guid.NewGuid(), Ref(issuedNumber)))
            .Should().Be(OriginalInvoiceStatus.KnownIssued);
        (await lookup.FindOriginalStatusAsync(Guid.NewGuid(), Ref(notIssuedNumber)))
            .Should().Be(OriginalInvoiceStatus.KnownNotIssued, "connue mais pas encore émise (avoir à mettre en attente).");
        (await lookup.FindOriginalStatusAsync(Guid.NewGuid(), Ref(Unique("FAC"))))
            .Should().Be(OriginalInvoiceStatus.Unknown, "facture d'origine inconnue = avoir orphelin (fail-safe).");
    }

    // ── F06 §5 : reprise sur timeout d'envoi (GetPotentiallySentDocuments) ───────────────────────────────

    [Fact]
    public async Task GetPotentiallySentDocuments_Returns_Only_Sending_Documents()
    {
        var harness = new DocumentsHarness(_fixture);
        var sending = DocumentTestData.Reconstituted(DocumentState.Sending, documentNumber: Unique("F"), payloadHash: Unique("h"));
        var issued = DocumentTestData.Reconstituted(DocumentState.Issued, documentNumber: Unique("F"), payloadHash: Unique("h"));
        await SeedAsync(harness, sending);
        await SeedAsync(harness, issued);

        var potentiallySent = await harness.Queries.GetPotentiallySentDocumentsAsync();

        potentiallySent.Should().Contain(d => d.Id == sending.Id, "l'issue de l'envoi est incertaine (F06 §5).");
        potentiallySent.Should().NotContain(d => d.Id == issued.Id, "un document émis a une issue connue.");
        potentiallySent.Should().OnlyContain(d => d.State == nameof(DocumentState.Sending));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<DuplicateCheckResult> CheckAsync(
        DocumentsHarness harness,
        string? supplierSiren,
        string documentNumber,
        string payloadHash)
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

    private static PivotDocumentRefDto Ref(string number) => new(number, new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc));

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
