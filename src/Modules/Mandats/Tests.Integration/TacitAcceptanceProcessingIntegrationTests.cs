namespace Liakont.Modules.Mandats.Tests.Integration;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Bascule tacite des auto-factures 389 (MND04, ADR-0024 §4) sur PostgreSQL réel (Testcontainers). SIG05 — la
/// bascule est PROJETÉE via DocumentApproval : le service énumère les candidats dus (purpose SelfBilledAcceptance)
/// puis bascule chacun sous verrou (re-vérification d'éligibilité anti-TOCTOU dans DocumentApproval). Produit
/// cartésien échéance null / échue / future, journalisation système atomique (operator_id null) dans
/// <c>document_approval_log</c>, et attribution par <c>company_id</c> sur ≥ 2 sociétés (INV-MANDATS-1 ; le service
/// est DB-wide en database-per-tenant et écrit chaque bascule sous le company_id porté par la ligne).
/// </summary>
[Collection("MandatsIntegration")]
public sealed class TacitAcceptanceProcessingIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PendingSince = Now.AddDays(-40);

    private readonly MandatsDatabaseFixture _fixture;

    public TacitAcceptanceProcessingIntegrationTests(MandatsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessDue_Switches_Only_Pending_With_Elapsed_Deadline_And_Logs_System_Transition()
    {
        var harness = new MandatsHarness(_fixture);

        // Deux sociétés (≥ 2 — INV-MANDATS-1). Le service traite toute la base (database-per-tenant) et écrit
        // chaque bascule sous le company_id de la ligne.
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var dueA = Guid.NewGuid();                 // écrit + délai → échéance échue → bascule
        var nullDeadlineA = Guid.NewGuid();        // tacite/délai null → échéance null → jamais
        var boundaryB = Guid.NewGuid();            // échéance EXACTEMENT à now (deadline <= now) → bascule
        var futureB = Guid.NewGuid();              // échéance future → pas encore

        await OpenPendingAsync(harness, companyA, dueA, deadline: Now.AddDays(-1));
        await OpenPendingAsync(harness, companyA, nullDeadlineA, deadline: null);
        await OpenPendingAsync(harness, companyB, boundaryB, deadline: Now);
        await OpenPendingAsync(harness, companyB, futureB, deadline: Now.AddDays(1));

        var service = new TacitAcceptanceService(harness.ApprovalQueries, harness.Workflow, new FixedTimeProvider(Now));

        // Le service est volontairement DB-wide (database-per-tenant) et la fixture est PARTAGÉE par la
        // collection : on n'asserte donc QUE l'état par document (jamais le compteur global du résultat —
        // anti-pattern faux-vert sur fixture partagée, leçons pipeline). Le compteur de retour est couvert,
        // en isolation, par TacitAcceptanceServiceTests (unit).
        await service.ProcessDueAsync();

        await AssertStateAsync(harness, companyA, dueA, SelfBilledAcceptanceState.TacitlyAccepted);
        await AssertStateAsync(harness, companyB, boundaryB, SelfBilledAcceptanceState.TacitlyAccepted);
        await AssertStateAsync(harness, companyA, nullDeadlineA, SelfBilledAcceptanceState.PendingAcceptance);
        await AssertStateAsync(harness, companyB, futureB, SelfBilledAcceptanceState.PendingAcceptance);

        // Journal système (operator_id null) pour les bascules, sous le BON company_id.
        await AssertTacitTransitionLoggedAsync(harness, companyA, dueA);
        await AssertTacitTransitionLoggedAsync(harness, companyB, boundaryB);

        // Les non-éligibles n'ont que leur genèse (aucune transition).
        (await harness.AcceptanceQueries.GetAcceptanceLog(companyA, nullDeadlineA)).Should().HaveCount(1);
        (await harness.AcceptanceQueries.GetAcceptanceLog(companyB, futureB)).Should().HaveCount(1);
    }

    [Fact]
    public async Task ProcessDue_Is_Idempotent_Second_Run_Switches_Nothing()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, deadline: Now.AddDays(-1));

        var service = new TacitAcceptanceService(harness.ApprovalQueries, harness.Workflow, new FixedTimeProvider(Now));

        // Assertions PAR DOCUMENT (jamais le compteur global : fixture partagée, service DB-wide).
        await service.ProcessDueAsync();
        (await harness.AcceptanceQueries.GetAcceptance(companyId, documentId))!.State
            .Should().Be(nameof(SelfBilledAcceptanceState.TacitlyAccepted), "le premier passage bascule le document dû.");

        await service.ProcessDueAsync();
        (await harness.AcceptanceQueries.GetAcceptance(companyId, documentId))!.State
            .Should().Be(nameof(SelfBilledAcceptanceState.TacitlyAccepted),
                "une fois basculée, l'acceptation n'est plus en attente → un second passage ne re-bascule rien.");

        // Une SEULE transition journalisée (genèse + une bascule) : pas de double écriture (idempotence + append-only).
        var log = await harness.AcceptanceQueries.GetAcceptanceLog(companyId, documentId);
        log.Should().HaveCount(2, "genèse + une bascule tacite, jamais deux.");
    }

    private static Task OpenPendingAsync(
        MandatsHarness harness, Guid companyId, Guid documentId, DateTimeOffset? deadline)
        => harness.Commands.OpenPendingAsync(
            companyId, documentId, PendingSince, deadline, operatorId: null, "Ingestion (test)");

    private static async Task AssertStateAsync(
        MandatsHarness harness, Guid companyId, Guid documentId, SelfBilledAcceptanceState expected)
    {
        var dto = await harness.AcceptanceQueries.GetAcceptance(companyId, documentId);
        dto!.State.Should().Be(expected.ToString());
    }

    private static async Task AssertTacitTransitionLoggedAsync(MandatsHarness harness, Guid companyId, Guid documentId)
    {
        var log = await harness.AcceptanceQueries.GetAcceptanceLog(companyId, documentId);
        log.Should().Contain(
            e => e.FromState == nameof(SelfBilledAcceptanceState.PendingAcceptance)
                 && e.ToState == nameof(SelfBilledAcceptanceState.TacitlyAccepted)
                 && e.OperatorId == null,
            "chaque bascule tacite écrit une transition système (operator_id null) dans document_approval_log.");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
