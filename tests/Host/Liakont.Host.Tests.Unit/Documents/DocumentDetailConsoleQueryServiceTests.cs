namespace Liakont.Host.Tests.Unit.Documents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class DocumentDetailConsoleQueryServiceTests
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly string[] MentionsRegimeCodes = ["6"];

    /// <summary>JSON canonique d'un pivot TRANSMIS (déjà mappé) servant au repli sur le snapshot.</summary>
    private static readonly string TransmittedSnapshotJson = Liakont.Agent.Contracts.Serialization.CanonicalJson.Serialize(
        new PivotDocumentDto(
            sourceDocumentKind: "invoice",
            number: "2026-101",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "src/2026-101",
            supplier: null,
            totals: new PivotTotalsDto(totalNet: 800m, totalTax: 160m, totalGross: 960m),
            operationCategory: null,
            lines: new[]
            {
                new PivotLineDto(
                    description: "Vente transmise",
                    netAmount: 800m,
                    quantity: 1m,
                    sourceRegimeCodes: new[] { "FR-STD" },
                    taxes: new[] { new PivotLineTaxDto(taxAmount: 160m, rate: 20m, categoryCode: VatCategory.S) }),
            }));

    private static DocumentDetailConsoleQueryService Build(
        FakeDocumentQueries documents,
        FakeContentReplay? replay = null,
        DocumentMarginRecapDto? marginRecap = null,
        bool marginRecapThrows = false,
        Guid? reportedBatchId = null,
        bool emissionsThrow = false) =>
        new(
            documents,
            replay ?? new FakeContentReplay(),
            new FakeSender { Recap = marginRecap, Throws = marginRecapThrows },
            new FakeEmissionQueries(reportedBatchId, emissionsThrow),
            NullLogger<DocumentDetailConsoleQueryService>.Instance);

    [Fact]
    public async Task GetDetailAsync_Should_Return_Null_When_Document_Absent()
    {
        var fake = new FakeDocumentQueries { Document = null };
        var service = Build(fake);

        var result = await service.GetDetailAsync(DocId);

        result.Should().BeNull("un document introuvable se traduit par un détail null (page « introuvable »)");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Assemble_Document_Events_And_Archive()
    {
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-001", "Issued"),
            Events = [Event("DocumentDetected", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero))],
            Archive = new ArchiveReferenceDto
            {
                PackagePath = "vault/2026/2026-001.zip",
                PackageHash = "sha256:aaa",
                ChainHash = "sha256:bbb",
                ArchivedUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            },
        };
        var service = Build(fake);

        var result = await service.GetDetailAsync(DocId);

        result.Should().NotBeNull();
        result!.Document.DocumentNumber.Should().Be("2026-001");
        result.Events.Should().ContainSingle();
        result.Archive.Should().NotBeNull();
        result.IsArchived.Should().BeTrue("une référence d'archive existe");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Expose_Blocking_Reason_Only_When_Blocked()
    {
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-002", "Blocked"),
            Events =
            [
                Event("DocumentDetected", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)),
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Le SIREN de l'émetteur est invalide."),
            ],
        };
        var service = Build(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.BlockingReason.Should().Be("Le SIREN de l'émetteur est invalide.");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Hide_Stale_Blocking_Reason_When_Not_Blocked()
    {
        // Le document a été bloqué PUIS débloqué/émis : le motif de blocage est périmé et ne doit pas
        // s'afficher (message opérateur trompeur, CLAUDE.md n°12) — l'historique complet reste dans Events.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-003", "Issued"),
            Events =
            [
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Régime TVA non mappé."),
                Event("DocumentIssued", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)),
            ],
        };
        var service = Build(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.BlockingReason.Should().BeNull("le motif est périmé sur un document émis");
        result.Events.Should().HaveCount(2, "l'historique complet reste disponible");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Pick_The_Latest_Blocking_Event()
    {
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-004", "Blocked"),
            Events =
            [
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Premier motif."),
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), detail: "Motif le plus récent."),
            ],
        };
        var service = Build(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.BlockingReason.Should().Be("Motif le plus récent.");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Prefer_The_Latest_Recheck_Reason_Over_The_Original_Block()
    {
        // FIX02 : après une re-vérification restée bloquée, le motif COURANT affiché est le dernier évalué
        // (événement DocumentRecheckedStillBlocked), jamais le motif initial périmé.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-006", "Blocked"),
            Events =
            [
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Motif initial (table TVA absente)."),
                Event("DocumentRecheckedStillBlocked", new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), detail: "Motif réévalué (acheteur professionnel)."),
            ],
        };
        var service = Build(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.BlockingReason.Should().Be("Motif réévalué (acheteur professionnel).", "le motif courant = le dernier événement porteur d'un motif (recheck inclus), pas le motif initial périmé");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Report_Not_Archived_When_No_Archive_Reference()
    {
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-005", "Detected"),
            Events = [],
            Archive = null,
        };
        var service = Build(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.IsArchived.Should().BeFalse();
        result.Archive.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailAsync_Should_Project_Lines_From_The_Read_Time_Replay_Before_Transmission()
    {
        // BUG-5 : un document BLOQUÉ (jamais transmis, donc sans PayloadSnapshot) montre tout de même ses lignes,
        // projetées depuis le pivot SOURCE relu au read-time (rejeu Pipeline) — régime source présent, catégorie
        // VIDE (le mapping a bloqué : diagnostic factuel, jamais inventé). Le rejeu est consommé AVANT les events.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-100", "Blocked"),
            Events = [Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Régime TVA non mappé.")],
        };
        var replay = FakeContentReplay.Returning(SourcePivot("Adjudication lot 12", sourceRegime: "6", netAmount: 500m));

        var result = await Build(fake, replay).GetDetailAsync(DocId);

        result!.Content.HasLines.Should().BeTrue("le détail des lignes est visible dès l'état Bloqué (rejeu read-time)");
        result.Content.Lines.Should().ContainSingle();
        var line = result.Content.Lines[0];
        line.Label.Should().Be("Adjudication lot 12");
        line.NetAmount.Should().Be(500m);
        line.SourceRegime.Should().Be("6", "le régime source lu est restitué (diagnostic du blocage)");
        line.Category.Should().Be("—", "le mapping a bloqué : aucune catégorie n'est devinée (CLAUDE.md n°2)");
        line.Vatex.Should().Be("—", "aucun VATEX n'est inventé sur un document bloqué");
        replay.Calls.Should().ContainSingle().Which.Should().Be(DocId);
    }

    [Fact]
    public async Task GetDetailAsync_Should_Surface_Billing_Mentions_From_The_Read_Time_Replay()
    {
        // BUG-26 : les mentions de facturation EFFECTIVES portées par le pivot d'affichage (le rejeu read-time les a
        // enrichies depuis le défaut tenant quand le document ne les porte pas) remontent dans le contenu projeté :
        // termes de paiement (BT-20) + notes légales FR (PMD/PMT/AAB). Le contenu vient du pivot, jamais inventé.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-110", "Blocked"),
            Events = [Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Régime TVA non mappé.")],
        };
        var pivotWithMentions = SourcePivotWithMentions(
            paymentTerms: "Paiement à 30 jours.",
            notes: new[]
            {
                new PivotDocumentNoteDto("Pénalités de retard au taux légal.", "PMD"),
                new PivotDocumentNoteDto("Indemnité forfaitaire de 40 €.", "PMT"),
                new PivotDocumentNoteDto("Pas d'escompte.", "AAB"),
            });
        var replay = FakeContentReplay.Returning(pivotWithMentions);

        var result = await Build(fake, replay).GetDetailAsync(DocId);

        result!.Content.PaymentTerms.Should().Be("Paiement à 30 jours.");
        result.Content.HasMentions.Should().BeTrue();
        result.Content.Notes.Should().HaveCount(3);
        result.Content.Notes.Should().ContainSingle(n => n.SubjectCode == "PMD" && n.Content == "Pénalités de retard au taux légal.");
        result.Content.Notes.Should().ContainSingle(n => n.SubjectCode == "PMT" && n.Content == "Indemnité forfaitaire de 40 €.");
        result.Content.Notes.Should().ContainSingle(n => n.SubjectCode == "AAB" && n.Content == "Pas d'escompte.");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Leave_Mentions_Empty_When_The_Replay_Pivot_Carries_None()
    {
        // BUG-26 : un pivot sans mention → contenu sans mention (HasMentions faux) ; la vue affiche son hint, jamais
        // une mention inventée (CLAUDE.md n°2).
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-111", "Blocked"),
            Events = [Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Régime TVA non mappé.")],
        };
        var replay = FakeContentReplay.Returning(SourcePivot("Adjudication lot 12", sourceRegime: "6", netAmount: 500m));

        var result = await Build(fake, replay).GetDetailAsync(DocId);

        result!.Content.HasMentions.Should().BeFalse("le pivot ne porte aucune mention → rien n'est inventé");
        result.Content.PaymentTerms.Should().BeNull();
        result.Content.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDetailAsync_Should_Prefer_The_Transmitted_Snapshot_Over_The_Read_Time_Replay()
    {
        // BUG-5 (P2 review) : un document TRANSMIS (Issued) porte un PayloadSnapshot = la VÉRITÉ envoyée à la PA.
        // Le détail affiché DOIT être ce snapshot, JAMAIS une re-dérivation depuis la table de mapping COURANTE
        // (qui a pu changer depuis l'émission). Le rejeu read-time n'est donc même PAS tenté quand un snapshot existe.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-101", "Issued"),
            Events =
            [
                Event("DocumentIssued", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), payloadSnapshot: TransmittedSnapshotJson),
            ],
        };

        // Même si le rejeu était disponible avec un AUTRE contenu, le snapshot transmis doit primer.
        var replay = FakeContentReplay.Returning(
            SourcePivot("Contenu re-dérivé (NE DOIT PAS s'afficher)", sourceRegime: "6", netAmount: 1m));

        var result = await Build(fake, replay).GetDetailAsync(DocId);

        result!.Content.HasLines.Should().BeTrue("le snapshot transmis restitue les lignes");
        result.Content.Lines.Should().ContainSingle().Which.Label.Should().Be("Vente transmise");
        replay.Calls.Should().BeEmpty("un document transmis affiche sa vérité d'audit, jamais un rejeu re-dérivé");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Show_Empty_Content_When_Not_Transmitted_And_Replay_Throws()
    {
        // BUG-5 (robustesse) : pour un document NON transmis (sans snapshot), le rejeu read-time est tenté ; son
        // échec ne casse jamais le détail → contenu vide (la vue affiche sa note, jamais de ligne inventée).
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-102", "Blocked"),
            Events = [Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Régime TVA non mappé.")],
        };
        var replay = FakeContentReplay.Throwing();

        var result = await Build(fake, replay).GetDetailAsync(DocId);

        result!.Content.HasLines.Should().BeFalse("un rejeu en échec ne montre aucune ligne (note de la vue)");
        replay.Calls.Should().ContainSingle("le rejeu est tenté faute de snapshot transmis");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Surface_The_Margin_Recap_For_A_Margin_Document()
    {
        // Le récap de marge (aide à la déclaration de TVA) est calculé par le module Pipeline sur le pivot affiché
        // puis remonté tel quel dans le modèle (projection pure côté Host). Ici le handler (fake) renvoie un récap.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("9000004", "Issued"),
            Events = [Event("DocumentIssued", new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero), payloadSnapshot: TransmittedSnapshotJson)],
        };
        var recap = new DocumentMarginRecapDto
        {
            BuyerFeesTtc = 10m,
            SellerFeesTtc = 0m,
            MarginTtc = 10m,
            BaseHt = 8.33m,
            Tva = 1.67m,
            RatePercent = 20m,
        };

        var result = await Build(fake, marginRecap: recap).GetDetailAsync(DocId);

        result!.MarginRecap.Should().NotBeNull();
        result.MarginRecap!.BuyerFeesTtc.Should().Be(10m);
        result.MarginRecap.SellerFeesTtc.Should().Be(0m);
        result.MarginRecap.MarginTtc.Should().Be(10m);
        result.MarginRecap.BaseHt.Should().Be(8.33m);
        result.MarginRecap.Tva.Should().Be(1.67m);
        result.MarginRecap.RatePercent.Should().Be(20m);
    }

    [Fact]
    public async Task GetDetailAsync_Should_Stay_Available_When_The_Margin_Recap_Resolution_Fails()
    {
        // Robustesse : le récap de marge est une aide AUXILIAIRE. Une panne de sa résolution (mapping/tenant) ne
        // doit JAMAIS casser la lecture du détail (vérité d'audit) — le détail reste assemblé, sans récap.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("9000004", "Issued"),
            Events = [Event("DocumentIssued", new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero), payloadSnapshot: TransmittedSnapshotJson)],
        };

        var result = await Build(fake, marginRecapThrows: true).GetDetailAsync(DocId);

        result.Should().NotBeNull("une panne du récap n'empêche pas de consulter le document");
        result!.Content.HasLines.Should().BeTrue("le contenu transmis reste affiché");
        result.MarginRecap.Should().BeNull("le récap est omis sur panne, jamais inventé");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Surface_The_B2c_Reported_Batch_When_The_Document_Was_E_Reported()
    {
        // BUG-24 : un document RÉELLEMENT e-reporté (Issued) via la transmission AGRÉGÉE porte le lien vers son lot
        // d'émission → la fiche affiche « E-reporté » + le lien et masque l'envoi par la voie document. Read-time,
        // aucune transition d'état (le document reste « prêt à l'envoi » côté domaine — voie b, lien doc ↔ lot).
        var batchId = Guid.NewGuid();
        var fake = new FakeDocumentQueries { Document = Doc("9000004", "ReadyToSend"), Events = [] };

        var result = await Build(fake, reportedBatchId: batchId).GetDetailAsync(DocId);

        result!.B2cReportedBatchId.Should().Be(batchId);
    }

    [Fact]
    public async Task GetDetailAsync_Should_Leave_The_B2c_Reported_Batch_Null_When_The_Document_Was_Not_E_Reported()
    {
        var fake = new FakeDocumentQueries { Document = Doc("9000004", "ReadyToSend"), Events = [] };

        var result = await Build(fake).GetDetailAsync(DocId);

        result!.B2cReportedBatchId.Should().BeNull("le document n'a pas (encore) été e-reporté → aucun lien, l'envoi reste proposé");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Stay_Available_When_The_B2c_Reporting_Resolution_Fails()
    {
        // Robustesse : l'état d'e-reporting est une RÉFLEXION auxiliaire. Une panne de sa résolution (base/tenant) ne
        // doit JAMAIS casser la lecture du détail (vérité d'audit) — le détail reste assemblé, sans lien e-reporté.
        var fake = new FakeDocumentQueries { Document = Doc("9000004", "ReadyToSend"), Events = [] };

        var result = await Build(fake, emissionsThrow: true).GetDetailAsync(DocId);

        result.Should().NotBeNull("une panne de la résolution e-reporting n'empêche pas de consulter le document");
        result!.B2cReportedBatchId.Should().BeNull("l'état e-reporté est omis sur panne, jamais inventé");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Leave_Margin_Recap_Null_Outside_The_Margin_Regime()
    {
        // Hors régime de la marge, le handler renvoie null (pas de récap configuré) → MarginRecap null, jamais inventé.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-200", "Issued"),
            Events = [Event("DocumentIssued", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), payloadSnapshot: TransmittedSnapshotJson)],
        };

        var result = await Build(fake).GetDetailAsync(DocId);

        result!.MarginRecap.Should().BeNull();
    }

    /// <summary>Pivot SOURCE minimal (catégorie/VATEX nuls — non mappés) pour exercer le rejeu read-time d'un document bloqué.</summary>
    private static PivotDocumentDto SourcePivot(string description, string sourceRegime, decimal netAmount) => new(
        sourceDocumentKind: "invoice",
        number: "2026-100",
        issueDate: new DateTime(2026, 6, 1),
        sourceReference: "src/2026-100",
        supplier: null,
        totals: new PivotTotalsDto(totalNet: netAmount, totalTax: 0m, totalGross: netAmount),
        operationCategory: null,
        lines: new[]
        {
            new PivotLineDto(
                description: description,
                netAmount: netAmount,
                quantity: 1m,
                sourceRegimeCodes: new[] { sourceRegime },
                taxes: new[] { new PivotLineTaxDto(taxAmount: 0m) }),
        });

    /// <summary>Pivot d'affichage portant des mentions de facturation (BT-20 + notes BG-1) — exerce leur remontée (BUG-26).</summary>
    private static PivotDocumentDto SourcePivotWithMentions(string paymentTerms, PivotDocumentNoteDto[] notes) => new(
        sourceDocumentKind: "invoice",
        number: "2026-110",
        issueDate: new DateTime(2026, 6, 1),
        sourceReference: "src/2026-110",
        supplier: null,
        totals: new PivotTotalsDto(totalNet: 500m, totalTax: 0m, totalGross: 500m),
        operationCategory: null,
        lines: new[]
        {
            new PivotLineDto(
                description: "Adjudication lot 12",
                netAmount: 500m,
                quantity: 1m,
                sourceRegimeCodes: MentionsRegimeCodes,
                taxes: new[] { new PivotLineTaxDto(taxAmount: 0m) }),
        },
        paymentTerms: paymentTerms,
        notes: notes);

    private static DocumentDto Doc(string number, string state) => new()
    {
        Id = DocId,
        SourceReference = $"src/{number}",
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        SupplierSiren = "123456782",
        CustomerName = "DUPONT J.",
        CustomerIsCompanyHint = false,
        TotalNet = 1000m,
        TotalTax = 162.80m,
        TotalGross = 1162.80m,
        State = state,
        PayloadHash = "sha256:payload",
        FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
    };

    private static DocumentEventDto Event(string type, DateTimeOffset when, string? detail = null, string? payloadSnapshot = null) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = DocId,
        TimestampUtc = when,
        EventType = type,
        Detail = detail,
        PayloadSnapshot = payloadSnapshot,
    };

    // Fake configurable d'IDocumentQueries : seules les 3 lectures du détail sont implémentées ; le reste
    // n'est jamais appelé par le service (NotSupported documente le contrat réellement utilisé).
    private sealed class FakeDocumentQueries : IDocumentQueries
    {
        public DocumentDto? Document { get; init; }

        public IReadOnlyList<DocumentEventDto> Events { get; init; } = [];

        public ArchiveReferenceDto? Archive { get; init; }

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Document);

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Events);

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Archive);

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Fake du rejeu read-time (BUG-5) : renvoie un pivot relu, « indisponible » (repli snapshot) ou lève (robustesse).
    private sealed class FakeContentReplay : IDocumentContentReplayService
    {
        private readonly DocumentContentReplay? _result;
        private readonly bool _throws;

        public FakeContentReplay(DocumentContentReplay? result = null, bool throws = false)
        {
            _result = result;
            _throws = throws;
        }

        public List<Guid> Calls { get; } = [];

        public static FakeContentReplay Returning(PivotDocumentDto pivot) => new(DocumentContentReplay.From(pivot));

        public static FakeContentReplay Unavailable() => new(DocumentContentReplay.Unavailable);

        public static FakeContentReplay Throwing() => new(throws: true);

        public Task<DocumentContentReplay> ReplayAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            Calls.Add(documentId);
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé du rejeu read-time.");
            }

            return Task.FromResult(_result ?? DocumentContentReplay.Unavailable);
        }
    }

    // Fake d'IB2cMarginEmissionQueries (BUG-24) : seule GetIssuedEmissionBatchForDocumentAsync est exercée par le
    // service de détail ; renvoie le lot e-reporté configuré (ou null), ou lève (robustesse). Les autres lectures ne
    // sont pas appelées par le détail (NotSupported documente le contrat réellement utilisé).
    private sealed class FakeEmissionQueries : IB2cMarginEmissionQueries
    {
        private readonly Guid? _reportedBatchId;
        private readonly bool _throws;

        public FakeEmissionQueries(Guid? reportedBatchId, bool throws)
        {
            _reportedBatchId = reportedBatchId;
            _throws = throws;
        }

        public Task<Guid?> GetIssuedEmissionBatchForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de la résolution de l'état d'e-reporting B2C.");
            }

            return Task.FromResult(_reportedBatchId);
        }

        public Task<IReadOnlyList<B2cMarginEmissionAggregateDto>> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<B2cMarginEmissionDetailDto?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Fake d'ISender : renvoie le récap de marge configuré pour GetDocumentMarginRecapQuery (null sinon), comme le
    // ferait le handler Pipeline hors régime de la marge. Le calcul fiscal réel est testé côté module (handler/résolveur).
    private sealed class FakeSender : ISender
    {
        public DocumentMarginRecapDto? Recap { get; init; }

        public bool Throws { get; init; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetDocumentMarginRecapQuery)
            {
                if (Throws)
                {
                    throw new InvalidOperationException("Échec simulé de la résolution du récap de marge.");
                }

                return Task.FromResult((TResponse)(object?)Recap!);
            }

            return Task.FromResult(default(TResponse)!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
