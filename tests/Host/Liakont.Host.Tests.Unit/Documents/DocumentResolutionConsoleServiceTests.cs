namespace Liakont.Host.Tests.Unit.Documents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests unitaires de <see cref="DocumentResolutionConsoleService"/> (WEB03c) : validations d'entrée AVANT
/// le port (motif obligatoire, remplaçant absent/identique), report fidèle du résultat du port
/// (<c>IDocumentLifecycle</c>), journalisation d'audit UNIQUEMENT sur succès avec l'identité de l'opérateur
/// AUTHENTIFIÉ (jamais une valeur de l'UI), et sélecteur de remplacement excluant le document courant.
/// </summary>
public sealed class DocumentResolutionConsoleServiceTests
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReplacementId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveManually_With_Empty_Reason_Returns_ReasonRequired_Without_Touching_The_Port(string? reason)
    {
        var lifecycle = new FakeDocumentLifecycle();
        var audit = new CapturingActivityLogger();
        var service = NewService(lifecycle, audit: audit);

        var status = await service.ResolveManuallyAsync(DocId, reason);

        status.Should().Be(DocumentResolutionConsoleStatus.ReasonRequired);
        lifecycle.ResolveManuallyCalls.Should().Be(0, "le motif est validé avant l'appel du port (parité 400 de l'endpoint)");
        audit.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ResolveManually_On_Success_Calls_Port_With_Authenticated_Operator_And_Logs()
    {
        var userId = Guid.NewGuid();
        var lifecycle = new FakeDocumentLifecycle { ResolveOutcome = DocumentResolutionOutcome.Succeeded };
        var audit = new CapturingActivityLogger();
        var service = NewService(lifecycle, actor: Actor(userId), audit: audit);

        var status = await service.ResolveManuallyAsync(DocId, "Avoir orphelin.");

        status.Should().Be(DocumentResolutionConsoleStatus.Succeeded);
        lifecycle.LastResolveId.Should().Be(DocId);
        lifecycle.LastResolveReason.Should().Be("Avoir orphelin.");
        lifecycle.LastResolveOperator.Should().Be(userId.ToString(), "l'identité d'audit est l'opérateur authentifié, jamais une valeur de l'UI");
        lifecycle.LastResolveOperatorName.Should().Be("Alice Martin", "le nom d'affichage de l'opérateur authentifié est aussi capturé pour la piste d'audit (FIX305)");
        audit.Calls.Should().Be(1);
        audit.LastActivityType.Should().Be("documents.resolved_manually");
    }

    [Theory]
    [InlineData(DocumentResolutionOutcome.DocumentNotFound)]
    [InlineData(DocumentResolutionOutcome.InvalidState)]
    public async Task ResolveManually_Maps_Port_Refusal_And_Does_Not_Log(DocumentResolutionOutcome outcome)
    {
        var lifecycle = new FakeDocumentLifecycle { ResolveOutcome = outcome };
        var audit = new CapturingActivityLogger();
        var service = NewService(lifecycle, audit: audit);

        var status = await service.ResolveManuallyAsync(DocId, "Motif.");

        var expected = outcome == DocumentResolutionOutcome.DocumentNotFound
            ? DocumentResolutionConsoleStatus.DocumentNotFound
            : DocumentResolutionConsoleStatus.InvalidState;
        status.Should().Be(expected);
        audit.Calls.Should().Be(0, "aucune journalisation sur un refus du port");
    }

    [Fact]
    public async Task Supersede_With_Empty_Replacement_Returns_ReplacementRequired()
    {
        var lifecycle = new FakeDocumentLifecycle();
        var service = NewService(lifecycle);

        var status = await service.SupersedeAsync(DocId, Guid.Empty);

        status.Should().Be(DocumentResolutionConsoleStatus.ReplacementRequired);
        lifecycle.SupersedeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Supersede_With_Self_Returns_ReplacementIsSelf()
    {
        var lifecycle = new FakeDocumentLifecycle();
        var service = NewService(lifecycle);

        var status = await service.SupersedeAsync(DocId, DocId);

        status.Should().Be(DocumentResolutionConsoleStatus.ReplacementIsSelf);
        lifecycle.SupersedeCalls.Should().Be(0, "un document ne peut pas se remplacer lui-même");
    }

    [Fact]
    public async Task Supersede_On_Success_Calls_Port_And_Logs()
    {
        var userId = Guid.NewGuid();
        var lifecycle = new FakeDocumentLifecycle { SupersedeOutcome = DocumentResolutionOutcome.Succeeded };
        var audit = new CapturingActivityLogger();
        var service = NewService(lifecycle, actor: Actor(userId), audit: audit);

        var status = await service.SupersedeAsync(DocId, ReplacementId);

        status.Should().Be(DocumentResolutionConsoleStatus.Succeeded);
        lifecycle.LastSupersedeId.Should().Be(DocId);
        lifecycle.LastReplacementId.Should().Be(ReplacementId);
        lifecycle.LastSupersedeOperator.Should().Be(userId.ToString());
        lifecycle.LastSupersedeOperatorName.Should().Be("Alice Martin", "le nom d'affichage de l'opérateur est aussi capturé pour la piste d'audit (FIX305)");
        audit.Calls.Should().Be(1);
        audit.LastActivityType.Should().Be("documents.superseded");
    }

    [Fact]
    public async Task Supersede_Maps_ReplacementNotFound()
    {
        var lifecycle = new FakeDocumentLifecycle { SupersedeOutcome = DocumentResolutionOutcome.ReplacementNotFound };
        var service = NewService(lifecycle);

        var status = await service.SupersedeAsync(DocId, ReplacementId);

        status.Should().Be(DocumentResolutionConsoleStatus.ReplacementNotFound);
    }

    [Fact]
    public async Task SearchReplacementCandidates_Excludes_The_Rejected_Document_Itself()
    {
        var queries = new FakeDocumentQueries
        {
            Result = ListOf(Summary(DocId, "2026-002"), Summary(ReplacementId, "2026-099")),
        };
        var service = NewService(queries: queries);

        var candidates = await service.SearchReplacementCandidatesAsync(DocId, search: null);

        candidates.Should().ContainSingle();
        candidates[0].Id.Should().Be(ReplacementId, "le document rejeté est exclu de ses propres candidats");
    }

    [Fact]
    public async Task SearchReplacementCandidates_Trims_The_Search_Term()
    {
        var queries = new FakeDocumentQueries { Result = ListOf() };
        var service = NewService(queries: queries);

        await service.SearchReplacementCandidatesAsync(DocId, search: "  2026 ");

        queries.LastFilter!.Search.Should().Be("2026");
    }

    [Fact]
    public async Task SearchReplacementCandidates_Caps_The_Result()
    {
        var items = new List<DocumentSummaryDto>();
        for (var i = 0; i < 25; i++)
        {
            items.Add(Summary(Guid.NewGuid(), $"DOC-{i:D3}"));
        }

        var queries = new FakeDocumentQueries { Result = ListOf(items.ToArray()) };
        var service = NewService(queries: queries);

        var candidates = await service.SearchReplacementCandidatesAsync(DocId, search: null);

        candidates.Should().HaveCount(20, "le sélecteur est borné aux premiers candidats (la recherche affine)");
    }

    private static DocumentResolutionConsoleService NewService(
        IDocumentLifecycle? lifecycle = null,
        IDocumentQueries? queries = null,
        IActorContextAccessor? actor = null,
        IActivityLogger? audit = null) =>
        new(
            lifecycle ?? new FakeDocumentLifecycle(),
            queries ?? new FakeDocumentQueries(),
            actor ?? Actor(Guid.NewGuid()),
            audit ?? new CapturingActivityLogger());

    private static FakeActorContextAccessor Actor(Guid userId) => new(userId);

    private static DocumentListResult ListOf(params DocumentSummaryDto[] items) => new()
    {
        Items = items,
        Page = 1,
        PageSize = 21,
        TotalCount = items.Length,
        CountsByState = new Dictionary<string, int>(),
    };

    private static DocumentSummaryDto Summary(Guid id, string number) => new()
    {
        Id = id,
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 2),
        CustomerName = "MARTIN SAS",
        TotalGross = 3410.00m,
        State = "Detected",
        LastUpdateUtc = new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDocumentLifecycle : IDocumentLifecycle
    {
        public DocumentResolutionOutcome ResolveOutcome { get; init; } = DocumentResolutionOutcome.Succeeded;

        public DocumentResolutionOutcome SupersedeOutcome { get; init; } = DocumentResolutionOutcome.Succeeded;

        public int ResolveManuallyCalls { get; private set; }

        public int SupersedeCalls { get; private set; }

        public Guid LastResolveId { get; private set; }

        public string? LastResolveReason { get; private set; }

        public string? LastResolveOperator { get; private set; }

        public Guid LastSupersedeId { get; private set; }

        public Guid LastReplacementId { get; private set; }

        public string? LastSupersedeOperator { get; private set; }

        public string? LastResolveOperatorName { get; private set; }

        public string? LastSupersedeOperatorName { get; private set; }

        public Task<DocumentResolutionOutcome> ResolveManuallyAsync(
            Guid documentId, string reason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
        {
            ResolveManuallyCalls++;
            LastResolveId = documentId;
            LastResolveReason = reason;
            LastResolveOperator = operatorIdentity;
            LastResolveOperatorName = operatorName;
            return Task.FromResult(ResolveOutcome);
        }

        public Task<DocumentResolutionOutcome> SupersedeAsync(
            Guid documentId, Guid replacementDocumentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
        {
            SupersedeCalls++;
            LastSupersedeId = documentId;
            LastReplacementId = replacementDocumentId;
            LastSupersedeOperator = operatorIdentity;
            LastSupersedeOperatorName = operatorName;
            return Task.FromResult(SupersedeOutcome);
        }

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

        public Task ConfirmBuyerAsIndividualAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeDocumentQueries : IDocumentQueries
    {
        public DocumentListResult Result { get; init; } = new()
        {
            Items = Array.Empty<DocumentSummaryDto>(),
            Page = 1,
            PageSize = 21,
            TotalCount = 0,
            CountsByState = new Dictionary<string, int>(),
        };

        public DocumentListFilter? LastFilter { get; private set; }

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            return Task.FromResult(Result);
        }

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public FakeActorContextAccessor(Guid userId) => Current = new FakeActorContext(userId);

        public IActorContext Current { get; }

        private sealed class FakeActorContext : IActorContext
        {
            public FakeActorContext(Guid userId) => UserId = userId;

            public Guid UserId { get; }

            public Guid CorrelationId { get; } = Guid.NewGuid();

            public bool IsAuthenticated => true;

            public string? DisplayName => "Alice Martin";

            public string? Email => "alice@example.fr";

            public Guid? CompanyId { get; } = Guid.NewGuid();

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }

    private sealed class CapturingActivityLogger : IActivityLogger
    {
        public int Calls { get; private set; }

        public string? LastActivityType { get; private set; }

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
            Calls++;
            LastActivityType = activityType;
            return Task.CompletedTask;
        }
    }
}
