namespace Liakont.Host.Tests.Unit.Documents;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests unitaires du service d'actions de l'onglet Contrôles (WEB03b). Vérifie le miroir EXACT du contrat
/// des endpoints API02b : gardes d'état (verdict / re-vérification sur un document BLOQUÉ uniquement),
/// appel des ports (<see cref="IDocumentLifecycle"/> / <see cref="IDocumentRecheckService"/>), identité de
/// l'opérateur, journal d'audit, et messages opérateur en français — sans toucher à une base ni un pipeline.
/// </summary>
public sealed class DocumentControlActionsServiceTests
{
    private static readonly Guid OperatorId = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid CompanyId = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ConfirmB2c_On_A_Blocked_Document_Records_The_Verdict_And_Audits_Without_Changing_State()
    {
        var docId = Guid.NewGuid();
        var lifecycle = new RecordingLifecycle();
        var audit = new CapturingActivityLogger();
        var service = Build(
            new FakeDocumentQueries { ById = { [docId] = Doc(docId, "F-2026-001", "Blocked", companyHint: true) } },
            lifecycle,
            new FakeRecheckService(),
            audit);

        var result = await service.SubmitVerdictAsync(docId, ConsoleVerdict.ConfirmIndividualB2c);

        result.Success.Should().BeTrue();
        result.NewState.Should().Be("Blocked", "le verdict B2C ne change pas l'état (la re-vérification débloque)");
        result.Message.Should().Contain("F-2026-001").And.Contain("B2C");

        lifecycle.ConfirmedB2c.Should().ContainSingle()
            .Which.Should().Be((docId, OperatorId.ToString(), "Opérateur"), "le port reçoit l'identité (GUID) ET le nom d'affichage de l'opérateur capturé pour l'audit (FIX305)");
        lifecycle.ManualResolutions.Should().BeEmpty();
        audit.Entries.Should().ContainSingle().Which.ActivityType.Should().Be("documents.verdict_confirm_b2c");
    }

    [Fact]
    public async Task HandleManually_On_A_Blocked_Document_Resolves_Terminally_And_Audits()
    {
        var docId = Guid.NewGuid();
        var lifecycle = new RecordingLifecycle { ManualOutcome = DocumentResolutionOutcome.Succeeded };
        var audit = new CapturingActivityLogger();
        var service = Build(
            new FakeDocumentQueries { ById = { [docId] = Doc(docId, "F-2026-002", "Blocked", companyHint: true) } },
            lifecycle,
            new FakeRecheckService(),
            audit);

        var result = await service.SubmitVerdictAsync(docId, ConsoleVerdict.HandleManuallyB2b);

        result.Success.Should().BeTrue();
        result.NewState.Should().Be("ManuallyHandled");
        result.Message.Should().Contain("F-2026-002").And.Contain("B2B");

        lifecycle.ManualResolutions.Should().ContainSingle()
            .Which.Should().Match<(Guid DocumentId, string Reason, string OperatorIdentity, string? OperatorName)>(
                r => r.OperatorIdentity == OperatorId.ToString() && r.OperatorName == "Opérateur",
                "le traitement manuel reçoit l'identité (GUID) ET le nom d'affichage capturé (FIX305)");
        lifecycle.ConfirmedB2c.Should().BeEmpty();
        audit.Entries.Should().ContainSingle().Which.ActivityType.Should().Be("documents.verdict_handle_manually");
    }

    [Fact]
    public async Task Verdict_On_A_Non_Blocked_Document_Is_Refused_Without_Side_Effects()
    {
        var docId = Guid.NewGuid();
        var lifecycle = new RecordingLifecycle();
        var audit = new CapturingActivityLogger();
        var service = Build(
            new FakeDocumentQueries { ById = { [docId] = Doc(docId, "F-2026-003", "Issued", companyHint: true) } },
            lifecycle,
            new FakeRecheckService(),
            audit);

        var result = await service.SubmitVerdictAsync(docId, ConsoleVerdict.ConfirmIndividualB2c);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("bloqué").And.Contain("Issued").And.Contain("F-2026-003");
        lifecycle.ConfirmedB2c.Should().BeEmpty("aucun port n'est appelé sur un document non bloqué");
        audit.Entries.Should().BeEmpty("aucune action n'est journalisée sur un refus d'état");
    }

    [Fact]
    public async Task Verdict_On_A_Missing_Document_Is_Refused_As_Not_Found()
    {
        var lifecycle = new RecordingLifecycle();
        var service = Build(new FakeDocumentQueries(), lifecycle, new FakeRecheckService(), new CapturingActivityLogger());

        var result = await service.SubmitVerdictAsync(Guid.NewGuid(), ConsoleVerdict.ConfirmIndividualB2c);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("introuvable");
        lifecycle.ConfirmedB2c.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleManually_Refused_By_The_State_Machine_Returns_A_French_Failure_Without_Auditing()
    {
        var docId = Guid.NewGuid();
        var lifecycle = new RecordingLifecycle { ManualOutcome = DocumentResolutionOutcome.InvalidState };
        var audit = new CapturingActivityLogger();
        var service = Build(
            new FakeDocumentQueries { ById = { [docId] = Doc(docId, "F-2026-004", "Blocked", companyHint: true) } },
            lifecycle,
            new FakeRecheckService(),
            audit);

        var result = await service.SubmitVerdictAsync(docId, ConsoleVerdict.HandleManuallyB2b);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("F-2026-004");
        audit.Entries.Should().BeEmpty("un refus concurrent n'est pas journalisé comme une action aboutie");
    }

    [Fact]
    public async Task Recheck_That_Unblocks_Returns_ReadyToSend_And_Audits()
    {
        var docId = Guid.NewGuid();
        var recheck = new FakeRecheckService { Result = DocumentRecheckResult.ReadyToSend() };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, audit);

        var result = await service.RecheckAsync(docId);

        result.Success.Should().BeTrue();
        result.NewState.Should().Be("ReadyToSend");
        result.Message.Should().Contain("prêt à l'envoi");
        recheck.Calls.Should().ContainSingle().Which.Should().Be(docId);
        audit.Entries.Should().ContainSingle().Which.ActivityType.Should().Be("documents.rechecked");
    }

    [Fact]
    public async Task Recheck_That_Stays_Blocked_Surfaces_The_Fresh_Reason_And_Audits()
    {
        var recheck = new FakeRecheckService { Result = DocumentRecheckResult.StillBlocked("Régime TVA non mappé.") };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, audit);

        var result = await service.RecheckAsync(Guid.NewGuid());

        // Opération réussie (la re-vérification a tourné) mais le document reste bloqué : le motif frais est
        // montré à l'opérateur (CLAUDE.md n°12) ; la page rechargera ensuite l'onglet Contrôles.
        result.Success.Should().BeTrue();
        result.NewState.Should().Be("Blocked");
        result.Message.Should().Contain("reste bloqué").And.Contain("Régime TVA non mappé.");
        audit.Entries.Should().ContainSingle().Which.ActivityType.Should().Be("documents.rechecked");
    }

    [Fact]
    public async Task Recheck_Threads_The_Operator_Identity_To_The_Recheck_Service()
    {
        // FIX02 : la re-vérification est une action OPÉRATEUR ; son identité doit voyager jusqu'au service de
        // recheck pour être inscrite dans la piste d'audit append-only (auteur du geste).
        var recheck = new FakeRecheckService { Result = DocumentRecheckResult.StillBlocked("Régime TVA non mappé.") };
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, new CapturingActivityLogger());

        await service.RecheckAsync(Guid.NewGuid());

        recheck.Operators.Should().ContainSingle()
            .Which.Should().Be(OperatorId.ToString(), "l'identité de l'opérateur est threadée jusqu'au service de re-vérification (audit FIX02)");
        recheck.OperatorNames.Should().ContainSingle()
            .Which.Should().Be("Opérateur", "le nom d'affichage de l'opérateur est aussi threadé pour la piste d'audit (FIX305)");
    }

    [Fact]
    public async Task Recheck_On_A_Non_Blocked_Document_Is_Refused_Without_Auditing()
    {
        var recheck = new FakeRecheckService { Result = DocumentRecheckResult.NotBlocked("Issued") };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, audit);

        var result = await service.RecheckAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("bloqué").And.Contain("Issued");
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Recheck_On_A_Missing_Document_Is_Refused_As_Not_Found()
    {
        var recheck = new FakeRecheckService { Result = DocumentRecheckResult.NotFound() };
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, new CapturingActivityLogger());

        var result = await service.RecheckAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("introuvable");
    }

    [Fact]
    public async Task Recheck_With_Unavailable_Content_Tells_The_Operator_To_Re_Extract()
    {
        var recheck = new FakeRecheckService { Result = DocumentRecheckResult.ContentUnavailable("Blocked") };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, audit);

        var result = await service.RecheckAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("pas disponible").And.Contain("logiciel source");
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_Actions_Permission_Verdict_And_Recheck_Are_Refused_Without_Touching_Ports()
    {
        // Défense en profondeur : le chemin in-process porte sa propre garde (comme RequireAuthorization côté
        // endpoint), il ne dépend pas du seul masquage des boutons côté UI.
        var docId = Guid.NewGuid();
        var lifecycle = new RecordingLifecycle();
        var recheck = new FakeRecheckService { Result = DocumentRecheckResult.ReadyToSend() };
        var audit = new CapturingActivityLogger();
        var service = Build(
            new FakeDocumentQueries { ById = { [docId] = Doc(docId, "F-2026-009", "Blocked", companyHint: true) } },
            lifecycle,
            recheck,
            audit,
            canAct: false);

        var verdict = await service.SubmitVerdictAsync(docId, ConsoleVerdict.ConfirmIndividualB2c);
        var rechecked = await service.RecheckAsync(docId);

        verdict.Success.Should().BeFalse();
        verdict.Message.Should().Contain("liakont.actions");
        rechecked.Success.Should().BeFalse();
        lifecycle.ConfirmedB2c.Should().BeEmpty("aucun port n'est touché sans la permission d'action");
        recheck.Calls.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RecheckMany_Delegates_To_The_Core_With_Operator_Identity_And_Reports_Counters()
    {
        // FIX207 : le service console délègue la boucle + la décision de blocage au cœur Pipeline et journalise le
        // geste GROUPÉ (un seul fait d'activité) ; les compteurs « N débloqués, N restés bloqués » remontent.
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var recheck = new FakeRecheckService
        {
            BulkSummary = new() { Total = 3, Unblocked = 1, StillBlocked = 2, Unavailable = 0, Skipped = 0 },
        };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, audit);

        var result = await service.RecheckManyAsync(ids);

        result.Success.Should().BeTrue();
        result.Unblocked.Should().Be(1);
        result.StillBlocked.Should().Be(2);
        result.Message.Should().Contain("1 débloqué").And.Contain("2 restés bloqués");
        recheck.BulkIds.Should().BeEquivalentTo(ids, "la liste est déléguée telle quelle au cœur de re-vérification");
        recheck.BulkOperators.Should().ContainSingle()
            .Which.Should().Be(OperatorId.ToString(), "l'identité de l'opérateur voyage jusqu'au cœur (audit FIX02)");
        recheck.BulkOperatorNames.Should().ContainSingle()
            .Which.Should().Be("Opérateur", "le nom d'affichage de l'opérateur voyage aussi jusqu'au cœur (FIX305)");
        audit.Entries.Should().ContainSingle("le geste groupé est journalisé une seule fois")
            .Which.ActivityType.Should().Be("documents.rechecked_bulk");
    }

    [Fact]
    public async Task RecheckMany_Surfaces_Unavailable_And_Skipped_In_The_Counters()
    {
        var recheck = new FakeRecheckService
        {
            BulkSummary = new() { Total = 4, Unblocked = 1, StillBlocked = 1, Unavailable = 1, Skipped = 1 },
        };
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, new CapturingActivityLogger());

        var result = await service.RecheckManyAsync(new[] { Guid.NewGuid() });

        result.Message.Should().Contain("1 débloqué").And.Contain("1 resté bloqué")
            .And.Contain("indisponible").And.Contain("ignoré");
    }

    [Fact]
    public async Task RecheckMany_Without_Actions_Permission_Is_Refused_Without_Touching_The_Core()
    {
        // Défense en profondeur (comme la re-vérification unitaire) : pas de permission ⇒ aucun document touché.
        var recheck = new FakeRecheckService
        {
            BulkSummary = new() { Total = 2, Unblocked = 2, StillBlocked = 0, Unavailable = 0, Skipped = 0 },
        };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeDocumentQueries(), new RecordingLifecycle(), recheck, audit, canAct: false);

        var result = await service.RecheckManyAsync(new[] { Guid.NewGuid() });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("liakont.actions");
        recheck.BulkIds.Should().BeNull("aucun document n'est re-vérifié sans la permission d'action");
        audit.Entries.Should().BeEmpty();
    }

    private static DocumentControlActionsService Build(
        FakeDocumentQueries queries,
        IDocumentLifecycle lifecycle,
        IDocumentRecheckService recheck,
        IActivityLogger audit,
        bool canAct = true) =>
        new(queries, lifecycle, recheck, new StubActorContextAccessor(OperatorId, CompanyId), audit, new FakePermissionService(canAct));

    private static DocumentDto Doc(Guid id, string number, string state, bool companyHint, bool confirmedB2c = false) => new()
    {
        Id = id,
        SourceReference = $"src/{number}",
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        CustomerName = "ACME SARL",
        CustomerIsCompanyHint = companyHint,
        BuyerConfirmedAsIndividual = confirmedB2c,
        TotalNet = 100m,
        TotalTax = 20m,
        TotalGross = 120m,
        State = state,
        PayloadHash = "hash",
        FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeRecheckService : IDocumentRecheckService
    {
        public DocumentRecheckResult Result { get; set; } = DocumentRecheckResult.ReadyToSend();

        public List<Guid> Calls { get; } = [];

        public List<string> Operators { get; } = [];

        // Re-vérification en masse (FIX207) : synthèse configurable + capture des identifiants/opérateur reçus.
        public DocumentBulkRecheckSummary BulkSummary { get; set; } =
            new() { Total = 0, Unblocked = 0, StillBlocked = 0, Unavailable = 0, Skipped = 0 };

        public IReadOnlyList<Guid>? BulkIds { get; private set; }

        public List<string> BulkOperators { get; } = [];

        public List<string?> OperatorNames { get; } = [];

        public List<string?> BulkOperatorNames { get; } = [];

        public Task<DocumentRecheckResult> RecheckAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
        {
            Calls.Add(documentId);
            Operators.Add(operatorIdentity);
            OperatorNames.Add(operatorName);
            return Task.FromResult(Result);
        }

        public Task<DocumentBulkRecheckSummary> RecheckManyAsync(
            IReadOnlyList<Guid> documentIds, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
        {
            BulkIds = documentIds.ToList();
            BulkOperators.Add(operatorIdentity);
            BulkOperatorNames.Add(operatorName);
            return Task.FromResult(BulkSummary);
        }
    }

    private sealed class RecordingLifecycle : IDocumentLifecycle
    {
        public DocumentResolutionOutcome ManualOutcome { get; set; } = DocumentResolutionOutcome.Succeeded;

        public List<(Guid DocumentId, string OperatorIdentity, string? OperatorName)> ConfirmedB2c { get; } = [];

        public List<(Guid DocumentId, string Reason, string OperatorIdentity, string? OperatorName)> ManualResolutions { get; } = [];

        public Task ConfirmBuyerAsIndividualAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
        {
            ConfirmedB2c.Add((documentId, operatorIdentity, operatorName));
            return Task.CompletedTask;
        }

        public Task<DocumentResolutionOutcome> ResolveManuallyAsync(Guid documentId, string reason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
        {
            ManualResolutions.Add((documentId, reason, operatorIdentity, operatorName));
            return Task.FromResult(ManualOutcome);
        }

        // Membres non utilisés par le service WEB03b — non sollicités dans ces tests.
        public Task BlockAsync(Guid documentId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkReadyToSendAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentRecheckPersistOutcome> MarkReadyToSendByRecheckAsync(Guid documentId, string mappingVersion, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentRecheckPersistOutcome> RecordRecheckStillBlockedAsync(Guid documentId, string reevaluatedReason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentRecheckPersistOutcome> MarkBlockedByRecheckAsync(Guid documentId, string reevaluatedReason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task BeginSendingAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordPaSendingReferenceAsync(Guid documentId, string paDocumentId, string? paResponseSnapshot, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkIssuedAsync(Guid documentId, DocumentIssuanceSnapshots snapshots, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkRejectedByPaAsync(Guid documentId, DocumentRejectionSnapshots snapshots, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkTechnicalErrorAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkEReportedAsync(Guid documentId, Guid emissionBatchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentResolutionOutcome> SupersedeAsync(Guid documentId, Guid replacementDocumentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeDocumentQueries : IDocumentQueries
    {
        public Dictionary<Guid, DocumentDto> ById { get; } = [];

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ById.TryGetValue(id, out var doc) ? doc : null);

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingActivityLogger : IActivityLogger
    {
        public List<(string EntityType, string EntityId, string ActivityType, string Description)> Entries { get; } = [];

        public Task LogActivityAsync(
            string entityType,
            string entityId,
            string activityType,
            string description,
            string actorId,
            object? metadata = null,
            Guid? companyId = null,
            CancellationToken cancellationToken = default)
        {
            Entries.Add((entityType, entityId, activityType, description));
            return Task.CompletedTask;
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _canAct;

        public FakePermissionService(bool canAct) => _canAct = canAct;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _canAct && string.Equals(permission, "liakont.actions", StringComparison.Ordinal);
    }

    private sealed class StubActorContextAccessor : IActorContextAccessor
    {
        public StubActorContextAccessor(Guid userId, Guid? companyId) =>
            Current = new StubActorContext(userId, companyId);

        public IActorContext Current { get; }

        private sealed class StubActorContext : IActorContext
        {
            public StubActorContext(Guid userId, Guid? companyId)
            {
                UserId = userId;
                CompanyId = companyId;
            }

            public Guid UserId { get; }

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => "Opérateur";

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-ctl";
        }
    }
}
