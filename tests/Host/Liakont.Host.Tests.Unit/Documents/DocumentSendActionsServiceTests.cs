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
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Liakont.Modules.Pipeline.Contracts.Queries;
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

    // Horloge fixe du déclenchement (FIX05) : le run corrélé doit démarrer à cet instant ou après.
    private static readonly DateTimeOffset TriggerInstant = new(2026, 6, 11, 8, 0, 0, TimeSpan.Zero);

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

        // FIX202 : aucun run corrélé dans le journal (runs vides) → dégradation gracieuse renvoyant au journal
        // (plus de bandeau « déclenché » statique). Le nombre + le montant total restent tracés dans l'AUDIT.
        result.Message.Should().Contain("journal des traitements");

        queue.Enqueued.Should().ContainSingle();
        var trigger = queue.Enqueued[0];
        trigger.Payload.Should().BeOfType<SendTenantTrigger>()
            .Which.Should().BeEquivalentTo(new { TenantId, DryRun = false }, "un SEUL déclencheur mono-tenant (ADR-0016)");
        trigger.CompanyId.Should().Be(CompanyId);

        var entry = audit.Entries.Should().ContainSingle().Which;
        entry.ActivityType.Should().Be("documents.send_all_triggered");

        // Le total fr-FR utilise une espace insécable comme séparateur de milliers (« 1 162,80 ») : on vérifie
        // la partie contiguë « 162,80 » pour ne pas dépendre du séparateur. (Audit en « 0.00 » invariant.)
        entry.Description.Should().Contain("2 document").And.Contain("162.80");
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

        // FIX202 : aucun run corrélé (runs vides) → résultat = renvoi au journal, AVEC les documents écartés
        // restitués À CÔTÉ (suffixe « Ignoré(s) à la sélection »). Les 2 documents prêts sont prouvés par l'audit.
        result.Message.Should().Contain("journal des traitements");
        result.Message.Should().Contain("Ignoré").And.Contain("F-003").And.Contain("introuvable");

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
        // Aucun run clôturé dans le journal : le déclenchement réussit, l'audit est posé, et le résultat
        // dégrade gracieusement vers le journal — sans prétendre que l'envoi a abouti (FIX05).
        var (service, queue, audit) = Build(new FakeDocumentQueries());

        var result = await service.TriggerRunAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("journal des traitements");

        queue.Enqueued.Should().ContainSingle();
        queue.Enqueued[0].Payload.Should().BeOfType<SendTenantTrigger>()
            .Which.Should().BeEquivalentTo(new { TenantId, DryRun = false });
        audit.Entries.Should().ContainSingle().Which.ActivityType.Should().Be("pipeline.run_triggered");
    }

    [Fact]
    public async Task TriggerRun_Reports_The_Emitted_Count_When_The_Run_Sent_Documents()
    {
        // FIX05 : le run d'envoi manuel clôturé ayant émis 3 documents est remonté à l'opérateur.
        var run = SendRun(succeeded: 3, failed: 0, detail: "SEND : 3 émis, 0 en échec, 0 différés, 0 ignorés.");
        var (service, queue, _) = Build(new FakeDocumentQueries(), runs: new[] { run });

        var result = await service.TriggerRunAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("3 document(s) émis");
        queue.Enqueued.Should().ContainSingle("le run reste déclenché normalement");
    }

    [Fact]
    public async Task TriggerRun_Surfaces_The_Motif_And_Is_Not_A_Success_When_Nothing_Was_Sent()
    {
        // FIX05 (cœur du bug) : un run terminé SANS rien envoyer (aucun compte PA) ne doit PAS ressembler à
        // un succès — il est signalé (Success == false) avec le motif opérateur écrit par le pipeline.
        const string Motif = "SEND : aucun compte Plateforme Agréée actif pour ce tenant — aucun envoi. Action opérateur : configurez et activez un compte PA (Paramétrage › Plateforme Agréée).";
        var run = SendRun(succeeded: 0, failed: 0, detail: Motif);
        var (service, _, _) = Build(new FakeDocumentQueries(), runs: new[] { run });

        var result = await service.TriggerRunAsync();

        result.Success.Should().BeFalse("un run qui n'envoie rien ne ressemble pas à un succès");
        result.Message.Should().Contain("aucun document émis").And.Contain("aucun compte Plateforme Agréée actif");
    }

    [Fact]
    public async Task TriggerRun_Ignores_A_Scheduled_Or_Older_Run_And_Falls_Back_To_The_Journal()
    {
        // Corrélation : seule une exécution SEND MANUELLE clôturée démarrée à l'instant du déclenchement (ou
        // après) compte. Un run planifié, et un run manuel ANTÉRIEUR, sont ignorés → dégradation gracieuse.
        var scheduledAfter = SendRun(succeeded: 9, failed: 0, detail: "planifié", trigger: PipelineRunTrigger.Scheduled);
        var olderManual = SendRun(succeeded: 5, failed: 0, detail: "ancien", startedAt: TriggerInstant.AddMinutes(-1));
        var (service, _, _) = Build(new FakeDocumentQueries(), runs: new[] { scheduledAfter, olderManual });

        var result = await service.TriggerRunAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("journal des traitements");
        result.Message.Should().NotContain("9 document").And.NotContain("5 document");
    }

    [Fact]
    public async Task TriggerRun_With_Concurrent_Manual_Runs_Falls_Back_To_The_Journal_Without_Asserting_A_Count()
    {
        // FIX05 (review P2) : deux envois manuels du tenant clôturés dans la fenêtre (déclenchements concurrents).
        // Sans clé de corrélation, on n'attribue PAS un chiffre potentiellement faux — on renvoie au journal.
        var runA = SendRun(succeeded: 3, failed: 0, detail: "A");
        var runB = SendRun(succeeded: 5, failed: 0, detail: "B", startedAt: TriggerInstant.AddSeconds(1));
        var (service, _, _) = Build(new FakeDocumentQueries(), runs: new[] { runA, runB });

        var result = await service.TriggerRunAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("journal des traitements");
        result.Message.Should().NotContain("3 document").And.NotContain("5 document");
    }

    [Fact]
    public async Task TriggerRun_Mentions_Pending_Documents_When_The_Batch_Is_Not_Fully_Sent()
    {
        // FIX05 (review P2) : un run qui émet 3 documents mais en diffère 2 (staging absent) ne doit pas passer
        // pour intégralement envoyé — les documents restants sont signalés (renvoi au journal).
        var run = SendRun(succeeded: 3, failed: 0, detail: "SEND : 3 émis, 0 en échec, 2 différés (staging absent), 0 ignorés.", processed: 5);
        var (service, _, _) = Build(new FakeDocumentQueries(), runs: new[] { run });

        var result = await service.TriggerRunAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("3 document(s) émis").And.Contain("2 document(s) restent en attente");
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

    // ── FIX202 : « Envoyer la sélection » et « Tout envoyer » remontent le RÉSULTAT du run (comme « Lancer un
    // traitement », FIX05), au lieu d'un bandeau « déclenché » statique répété en boucle. Même corrélation (run
    // SEND manuel clôturé ≥ instant du déclenchement), même projection opérateur (émis / aucun envoi + motif). ──
    [Fact]
    public async Task SendAll_Reports_The_Run_Outcome_When_The_Run_Sent_Documents()
    {
        var queries = new FakeDocumentQueries();
        queries.ReadyToSend.Add(Summary(Guid.NewGuid(), "F-1", "ReadyToSend", 1000m));
        queries.ReadyToSend.Add(Summary(Guid.NewGuid(), "F-2", "ReadyToSend", 162.80m));
        var run = SendRun(succeeded: 2, failed: 0, detail: "SEND : 2 émis, 0 en échec.");
        var (service, queue, _) = Build(queries, runs: new[] { run });

        var result = await service.SendAllAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("2 document(s) émis");
        queue.Enqueued.Should().ContainSingle("le run reste déclenché normalement");
    }

    [Fact]
    public async Task SendAll_Surfaces_The_Motif_And_Is_Not_A_Success_When_Nothing_Was_Sent()
    {
        // Cœur du bug FIX202 : l'opérateur clique « Tout envoyer », le run se clôt sans rien émettre (SIREN non
        // publié) — il doit voir le MOTIF + l'action corrective, pas un « envoi déclenché » qui boucle.
        const string Motif = "SEND : SIREN non publié auprès de la PA — aucun envoi. Action opérateur : faites publier le SIREN auprès de la PA, puis relancez l'envoi.";
        var queries = new FakeDocumentQueries();
        queries.ReadyToSend.Add(Summary(Guid.NewGuid(), "F-1", "ReadyToSend", 1000m));
        var run = SendRun(succeeded: 0, failed: 0, detail: Motif);
        var (service, _, _) = Build(queries, runs: new[] { run });

        var result = await service.SendAllAsync();

        result.Success.Should().BeFalse("un envoi groupé qui n'émet rien ne ressemble pas à un succès");
        result.Message.Should().Contain("aucun document émis").And.Contain("SIREN non publié");
    }

    [Fact]
    public async Task SendSelection_Reports_The_Run_Outcome_When_The_Run_Sent_Documents()
    {
        var ready = Guid.NewGuid();
        var queries = new FakeDocumentQueries();
        queries.ById[ready] = Doc(ready, "F-001", "ReadyToSend");
        var run = SendRun(succeeded: 1, failed: 0, detail: "SEND : 1 émis.");
        var (service, queue, _) = Build(queries, runs: new[] { run });

        var result = await service.SendSelectionAsync(new[] { ready });

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("1 document(s) émis");
        queue.Enqueued.Should().ContainSingle();
    }

    [Fact]
    public async Task SendSelection_Surfaces_The_Motif_When_Nothing_Was_Sent()
    {
        const string Motif = "SEND : SIREN non publié auprès de la PA — aucun envoi. Action opérateur : faites publier le SIREN auprès de la PA, puis relancez l'envoi.";
        var ready = Guid.NewGuid();
        var queries = new FakeDocumentQueries();
        queries.ById[ready] = Doc(ready, "F-001", "ReadyToSend");
        var run = SendRun(succeeded: 0, failed: 0, detail: Motif);
        var (service, _, _) = Build(queries, runs: new[] { run });

        var result = await service.SendSelectionAsync(new[] { ready });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("aucun document émis").And.Contain("SIREN non publié");
    }

    [Fact]
    public async Task SendSelection_Appends_The_Skipped_Documents_Next_To_The_Run_Outcome()
    {
        // Les documents écartés (non prêts / introuvables) sont restitués À CÔTÉ du résultat du run, pas à sa place.
        var ready = Guid.NewGuid();
        var notReady = Guid.NewGuid();
        var queries = new FakeDocumentQueries();
        queries.ById[ready] = Doc(ready, "F-001", "ReadyToSend");
        queries.ById[notReady] = Doc(notReady, "F-003", "Blocked");
        var run = SendRun(succeeded: 1, failed: 0, detail: "SEND : 1 émis.");
        var (service, _, _) = Build(queries, runs: new[] { run });

        var result = await service.SendSelectionAsync(new[] { ready, notReady });

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("1 document(s) émis", "le résultat du run est restitué");
        result.Message.Should().Contain("Ignoré").And.Contain("F-003", "les documents écartés sont restitués à côté du résultat");
    }

    [Theory]
    [InlineData(4, 0, true, "4 document(s) émis")]
    [InlineData(3, 1, true, "3 document(s) émis, 1 en échec")]
    [InlineData(0, 2, false, "aucun document émis, 2 en échec")]
    [InlineData(0, 0, false, "aucun document émis")]
    public void DescribeSendRunOutcome_Maps_Counts_And_Motif_To_An_Operator_Message(
        int succeeded, int failed, bool expectedSuccess, string expectedFragment)
    {
        var run = SendRun(succeeded, failed, detail: "motif-pipeline");

        var result = DocumentSendActionsService.DescribeSendRunOutcome(run);

        result.Success.Should().Be(expectedSuccess);
        result.Message.Should().Contain(expectedFragment);
        if (!expectedSuccess)
        {
            result.Message.Should().Contain("motif-pipeline", "un run sans émission expose le motif du pipeline");
        }
    }

    private static (DocumentSendActionsService Service, CapturingJobQueue Queue, CapturingActivityLogger Audit) Build(
        FakeDocumentQueries queries,
        bool canAct = true,
        string? tenantId = TenantId,
        IReadOnlyList<PipelineRunLogDto>? runs = null)
    {
        var queue = new CapturingJobQueue();
        var scopeFactory = BuildScopeFactory(queue);
        var audit = new CapturingActivityLogger();
        var service = new DocumentSendActionsService(
            queries,
            scopeFactory,
            new StubActorContextAccessor(OperatorId, CompanyId, tenantId),
            audit,
            new FakePermissionService(canAct),
            new FakeRunQueries(runs ?? Array.Empty<PipelineRunLogDto>()),
            new FixedTimeProvider(TriggerInstant),
            new SendRunWaitPolicy(TimeSpan.Zero, MaxAttempts: 3)); // sonde sans délai réel (FIX05 — déterministe)
        return (service, queue, audit);
    }

    /// <summary>Construit une exécution SEND clôturée pour les tests de remontée du résultat (FIX05).
    /// <paramref name="processed"/> par défaut = émis + en échec ; le surplus représente les différés/ignorés.</summary>
    private static PipelineRunLogDto SendRun(
        int succeeded,
        int failed,
        string? detail,
        PipelineRunTrigger trigger = PipelineRunTrigger.Manual,
        DateTimeOffset? startedAt = null,
        int? processed = null) => new()
    {
        Id = Guid.NewGuid(),
        RunType = PipelineRunType.Send,
        Trigger = trigger,
        StartedAt = startedAt ?? TriggerInstant,
        CompletedAt = (startedAt ?? TriggerInstant).AddSeconds(2),
        DocumentsProcessed = processed ?? (succeeded + failed),
        DocumentsSucceeded = succeeded,
        DocumentsFailed = failed,
        Detail = detail,
    };

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

    private sealed class FakeRunQueries : IPipelineRunQueries
    {
        private readonly IReadOnlyList<PipelineRunLogDto> _runs;

        public FakeRunQueries(IReadOnlyList<PipelineRunLogDto> runs) => _runs = runs;

        // Le journal réel rend les exécutions les plus récentes en tête (started_at décroissant) ; on respecte
        // ce contrat pour que FirstOrDefault côté service sélectionne bien la plus récente qui matche.
        public Task<IReadOnlyList<PipelineRunLogDto>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PipelineRunLogDto> ordered = _runs.OrderByDescending(r => r.StartedAt).Take(limit).ToList();
            return Task.FromResult(ordered);
        }

        public Task<IReadOnlyList<PipelineRunLogDto>> GetRunsAsync(
            DateOnly? fromInclusive, DateOnly? toInclusive, int limit, CancellationToken cancellationToken = default) =>
            GetRecentRunsAsync(limit, cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
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
