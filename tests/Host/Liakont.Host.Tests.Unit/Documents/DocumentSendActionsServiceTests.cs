namespace Liakont.Host.Tests.Unit.Documents;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Job.Contracts;
using Xunit;

/// <summary>
/// Tests unitaires du service des actions d'envoi de la page Documents (WEB05). Vérifie le miroir EXACT du
/// contrat des endpoints API02a / runs-trigger : garde de permission (liakont.actions) et tenant résolu,
/// validation par document de la sélection, publication d'UN SEUL déclencheur mono-tenant
/// <see cref="SendTenantTrigger"/> (ADR-0016 ; jamais un fan-out, jamais un job par document), récapitulatif
/// <c>decimal</c> du « Tout envoyer », journal d'audit (codes partagés) et messages opérateur en français —
/// sans toucher à une base ni un pipeline.
/// </summary>
public sealed class DocumentSendActionsServiceTests
{
    private const string TenantId = "tenant-send";
    private static readonly Guid OperatorId = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid CompanyId = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task SummarizeReadyToSend_Counts_And_Sums_Across_Pages_In_Decimal()
    {
        // 150 documents prêts → 2 pages (PageSize=100) : on prouve que la boucle de pagination ne tronque pas.
        var queries = new FakeDocumentQueries();
        for (var i = 0; i < 150; i++)
        {
            queries.ReadyToSend.Add(Summary(Guid.NewGuid(), $"F-{i:0000}", "ReadyToSend", 100.05m));
        }

        var (service, _, _) = Build(queries);

        var summary = await service.SummarizeReadyToSendAsync();

        summary.Count.Should().Be(150);
        summary.TotalGross.Should().Be(150 * 100.05m, "le total TTC est exact en decimal (CLAUDE.md n°1)");
    }

    [Fact]
    public async Task SendAll_Publishes_One_Tenant_Trigger_And_Audits_With_Count_And_Total()
    {
        var queries = new FakeDocumentQueries();
        queries.ReadyToSend.Add(Summary(Guid.NewGuid(), "F-1", "ReadyToSend", 1000.00m));
        queries.ReadyToSend.Add(Summary(Guid.NewGuid(), "F-2", "ReadyToSend", 162.80m));
        var (service, queue, audit) = Build(queries);

        var result = await service.SendAllAsync();

        result.Success.Should().BeTrue();

        // Le total fr-FR utilise une espace insécable comme séparateur de milliers (« 1 162,80 ») : on vérifie
        // la partie contiguë « 162,80 » pour ne pas dépendre du séparateur.
        result.Message.Should().Contain("2 document").And.Contain("162,80");

        queue.Enqueued.Should().ContainSingle();
        var trigger = queue.Enqueued[0];
        trigger.Payload.Should().BeOfType<SendTenantTrigger>()
            .Which.Should().BeEquivalentTo(new { TenantId, DryRun = false }, "un SEUL déclencheur mono-tenant (ADR-0016)");
        trigger.CompanyId.Should().Be(CompanyId);

        audit.Entries.Should().ContainSingle()
            .Which.ActivityType.Should().Be("documents.send_all_triggered");
    }

    [Fact]
    public async Task SendAll_With_Nothing_Ready_Refuses_Without_Publishing_Or_Auditing()
    {
        var (service, queue, audit) = Build(new FakeDocumentQueries());

        var result = await service.SendAllAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("rien à envoyer");
        queue.Enqueued.Should().BeEmpty("aucun envoi déclenché quand il n'y a aucun document prêt");
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAll_Without_Actions_Permission_Is_Refused_Without_Publishing()
    {
        var queries = new FakeDocumentQueries();
        queries.ReadyToSend.Add(Summary(Guid.NewGuid(), "F-1", "ReadyToSend", 10m));
        var (service, queue, audit) = Build(queries, canAct: false);

        var result = await service.SendAllAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("liakont.actions");
        queue.Enqueued.Should().BeEmpty("défense en profondeur : le chemin in-process refuse sans la permission");
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAll_With_Unresolved_Tenant_Is_Refused_Without_Publishing()
    {
        var queries = new FakeDocumentQueries();
        queries.ReadyToSend.Add(Summary(Guid.NewGuid(), "F-1", "ReadyToSend", 10m));
        var (service, queue, _) = Build(queries, tenantId: null);

        var result = await service.SendAllAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Tenant non résolu");
        queue.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task SendSelection_Triggers_Once_And_Audits_Each_Ready_Document()
    {
        var ready1 = Guid.NewGuid();
        var ready2 = Guid.NewGuid();
        var notReady = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var queries = new FakeDocumentQueries();
        queries.ById[ready1] = Doc(ready1, "F-001", "ReadyToSend");
        queries.ById[ready2] = Doc(ready2, "F-002", "ReadyToSend");
        queries.ById[notReady] = Doc(notReady, "F-003", "Blocked");
        var (service, queue, audit) = Build(queries);

        var result = await service.SendSelectionAsync(new[] { ready1, ready2, notReady, missing });

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("2 document").And.Contain("Ignoré");
        result.Message.Should().Contain("F-003").And.Contain("introuvable");

        // ADR-0016 : un SEUL déclencheur pour toute la sélection (le SEND du tenant émet tous les ReadyToSend),
        // mais chaque document PRÊT est journalisé (parité d'audit avec POST /documents/{id}/send).
        queue.Enqueued.Should().ContainSingle();
        audit.Entries.Should().HaveCount(2);
        audit.Entries.Should().OnlyContain(e => e.ActivityType == "documents.send_triggered");
        audit.Entries.Select(e => e.EntityId).Should().BeEquivalentTo(new[] { ready1.ToString(), ready2.ToString() });
    }

    [Fact]
    public async Task SendSelection_With_No_Ready_Document_Refuses_Without_Publishing()
    {
        var blocked = Guid.NewGuid();
        var queries = new FakeDocumentQueries();
        queries.ById[blocked] = Doc(blocked, "F-010", "Blocked");
        var (service, queue, audit) = Build(queries);

        var result = await service.SendSelectionAsync(new[] { blocked });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Aucun document prêt").And.Contain("F-010");
        queue.Enqueued.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SendSelection_With_Empty_Selection_Asks_To_Select()
    {
        var (service, queue, _) = Build(new FakeDocumentQueries());

        var result = await service.SendSelectionAsync(Array.Empty<Guid>());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Sélectionnez au moins un document");
        queue.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task SendSelection_Without_Actions_Permission_Is_Refused_Without_Publishing()
    {
        var ready = Guid.NewGuid();
        var queries = new FakeDocumentQueries();
        queries.ById[ready] = Doc(ready, "F-1", "ReadyToSend");
        var (service, queue, audit) = Build(queries, canAct: false);

        var result = await service.SendSelectionAsync(new[] { ready });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("liakont.actions");
        queue.Enqueued.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task TriggerRun_Publishes_A_Tenant_Trigger_And_Audits()
    {
        var (service, queue, audit) = Build(new FakeDocumentQueries());

        var result = await service.TriggerRunAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Traitement déclenché");

        queue.Enqueued.Should().ContainSingle();
        queue.Enqueued[0].Payload.Should().BeOfType<SendTenantTrigger>()
            .Which.Should().BeEquivalentTo(new { TenantId, DryRun = false });
        audit.Entries.Should().ContainSingle().Which.ActivityType.Should().Be("pipeline.run_triggered");
    }

    [Fact]
    public async Task TriggerRun_Without_Actions_Permission_Is_Refused_Without_Publishing()
    {
        var (service, queue, audit) = Build(new FakeDocumentQueries(), canAct: false);

        var result = await service.TriggerRunAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("liakont.actions");
        queue.Enqueued.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    private static (DocumentSendActionsService Service, CapturingJobQueue Queue, CapturingActivityLogger Audit) Build(
        FakeDocumentQueries queries,
        bool canAct = true,
        string? tenantId = TenantId)
    {
        var queue = new CapturingJobQueue();
        var scopeFactory = BuildScopeFactory(queue);
        var audit = new CapturingActivityLogger();
        var service = new DocumentSendActionsService(
            queries,
            scopeFactory,
            new StubActorContextAccessor(OperatorId, CompanyId, tenantId),
            audit,
            new FakePermissionService(canAct));
        return (service, queue, audit);
    }

    /// <summary>
    /// Fabrique de scope RÉELLE (conteneur Microsoft.Extensions.DependencyInjection) résolvant la file factice :
    /// reproduit fidèlement le chemin des endpoints (<c>CreateAsyncScope().GetRequiredService&lt;IJobQueue&gt;()</c>).
    /// </summary>
    private static IServiceScopeFactory BuildScopeFactory(IJobQueue queue)
    {
        var provider = new ServiceCollection()
            .AddSingleton(queue)
            .BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static DocumentSummaryDto Summary(Guid id, string number, string state, decimal totalGross) => new()
    {
        Id = id,
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        CustomerName = "ACME SARL",
        TotalGross = totalGross,
        State = state,
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static DocumentDto Doc(Guid id, string number, string state) => new()
    {
        Id = id,
        SourceReference = $"src/{number}",
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        CustomerName = "ACME SARL",
        CustomerIsCompanyHint = false,
        BuyerConfirmedAsIndividual = false,
        TotalNet = 100m,
        TotalTax = 20m,
        TotalGross = 120m,
        State = state,
        PayloadHash = "hash",
        FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDocumentQueries : IDocumentQueries
    {
        public Dictionary<Guid, DocumentDto> ById { get; } = [];

        public List<DocumentSummaryDto> ReadyToSend { get; } = [];

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ById.TryGetValue(id, out var doc) ? doc : null);

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var matches = ReadyToSend.Where(d => string.Equals(d.State, state, StringComparison.Ordinal));
            var pageItems = matches.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<IReadOnlyList<DocumentSummaryDto>>(pageItems);
        }

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingJobQueue : IJobQueue
    {
        public List<(object Payload, Guid? CompanyId)> Enqueued { get; } = [];

        public Task<Guid> EnqueueAsync<T>(
            T payload,
            int priority = 0,
            DateTimeOffset? scheduledAt = null,
            Guid? companyId = null,
            CancellationToken ct = default)
        {
            Enqueued.Add((payload!, companyId));
            return Task.FromResult(Guid.NewGuid());
        }
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
        public StubActorContextAccessor(Guid userId, Guid? companyId, string? tenantId) =>
            Current = new StubActorContext(userId, companyId, tenantId);

        public IActorContext Current { get; }

        private sealed class StubActorContext : IActorContext
        {
            public StubActorContext(Guid userId, Guid? companyId, string? tenantId)
            {
                UserId = userId;
                CompanyId = companyId;
                TenantId = tenantId;
            }

            public Guid UserId { get; }

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => "Opérateur";

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId { get; }
        }
    }
}
