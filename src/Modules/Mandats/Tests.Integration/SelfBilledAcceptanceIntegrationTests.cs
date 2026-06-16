namespace Liakont.Modules.Mandats.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.Mandats.Domain.Entities;
using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Workflow d'acceptation des auto-factures 389 (ADR-0024, F15 §2.3) sur PostgreSQL réel (Testcontainers).
/// SIG05 — l'acceptation est PROJETÉE via le module générique DocumentApproval (purpose SelfBilledAcceptance) :
/// round-trip de l'état projeté (INV-ACCEPT-4), journal append-only DÉSORMAIS dans
/// <c>documentapproval.document_approval_log</c> (INV-ACCEPT-5 amendé ; UPDATE/DELETE/TRUNCATE rejetés par
/// trigger base), unicité de la tentative active (ConflictException), isolation tenant sur ≥ 2 sociétés
/// (INV-MANDATS-1). La companion fiscale (<c>mandats.self_billed_acceptances</c>) ne porte plus que
/// allocated_number/pending_since.
/// </summary>
[Collection("MandatsIntegration")]
public sealed class SelfBilledAcceptanceIntegrationTests
{
    private static readonly DateTimeOffset PendingSince = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = PendingSince.AddDays(30);

    private readonly MandatsDatabaseFixture _fixture;

    public SelfBilledAcceptanceIntegrationTests(MandatsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Open_And_Get_RoundTrips_Pending_With_Deadline()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        var dto = await harness.AcceptanceQueries.GetAcceptance(companyId, documentId);
        dto.Should().NotBeNull();
        dto!.State.Should().Be(nameof(SelfBilledAcceptanceState.PendingAcceptance));
        dto.AllocatedNumber.Should().BeNull("le BT-1 fiscal est alloué par MND05, jamais ici.");
        dto.PendingSince.Should().Be(PendingSince);
        dto.DeadlineUtc.Should().Be(Deadline, "l'échéance (timestamptz) doit faire un round-trip exact.");
        dto.IsAccepted.Should().BeFalse("PendingAcceptance n'ouvre pas le gate.");
    }

    [Fact]
    public async Task Open_Writes_Genesis_Log_Line()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        var log = await harness.AcceptanceQueries.GetAcceptanceLog(companyId, documentId);
        log.Should().HaveCount(1, "la création écrit une ligne de genèse (INV-ACCEPT-5, document_approval_log).");
        log[0].FromState.Should().BeNull("la genèse n'a pas d'état « avant ».");
        log[0].ToState.Should().Be(nameof(SelfBilledAcceptanceState.PendingAcceptance));
    }

    [Fact]
    public async Task Express_Acceptance_Persists_State_And_Logs_Transition()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        await harness.Commands.AcceptExpresslyAsync(companyId, documentId, operatorId, "Opérateur de test");

        var dto = await harness.AcceptanceQueries.GetAcceptance(companyId, documentId);
        dto!.State.Should().Be(nameof(SelfBilledAcceptanceState.Accepted));
        dto.IsAccepted.Should().BeTrue("Accepted ouvre le gate.");

        // Genèse + transition : « pas de transition sans ligne de journal » (INV-ACCEPT-5), dans document_approval_log.
        var log = await harness.AcceptanceQueries.GetAcceptanceLog(companyId, documentId);
        log.Should().HaveCount(2);
        log[0].FromState.Should().Be(nameof(SelfBilledAcceptanceState.PendingAcceptance));
        log[0].ToState.Should().Be(nameof(SelfBilledAcceptanceState.Accepted));
        log[0].OperatorId.Should().Be(operatorId, "une acceptation expresse porte un opérateur.");
    }

    [Fact]
    public async Task Tacit_Acceptance_Is_System_Transition_Without_Operator()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        // Échéance échue (now = Deadline) → bascule tacite, transition SYSTÈME (operator_id null).
        var transitioned = await harness.Workflow.RecordTacitValidationIfDueAsync(
            companyId, documentId, ValidationPurpose.SelfBilledAcceptance, Deadline, "Bascule tacite (job)");
        transitioned.Should().BeTrue("l'échéance est échue (now ≥ deadline).");

        var dto = await harness.AcceptanceQueries.GetAcceptance(companyId, documentId);
        dto!.State.Should().Be(nameof(SelfBilledAcceptanceState.TacitlyAccepted));
        dto.IsAccepted.Should().BeTrue();

        var log = await harness.AcceptanceQueries.GetAcceptanceLog(companyId, documentId);
        log[0].ToState.Should().Be(nameof(SelfBilledAcceptanceState.TacitlyAccepted));
        log[0].OperatorId.Should().BeNull("une bascule tacite est une transition système, sans opérateur humain.");
    }

    [Fact]
    public async Task Tacit_Acceptance_NotDue_Before_Deadline_Does_Nothing()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        // Échéance NON échue (now < deadline) → pas de bascule (re-vérification sous verrou).
        var transitioned = await harness.Workflow.RecordTacitValidationIfDueAsync(
            companyId, documentId, ValidationPurpose.SelfBilledAcceptance, PendingSince, "Bascule tacite (job)");

        transitioned.Should().BeFalse("une échéance future ne bascule pas.");
        (await harness.AcceptanceQueries.GetAcceptance(companyId, documentId))!.State
            .Should().Be(nameof(SelfBilledAcceptanceState.PendingAcceptance));
    }

    [Fact]
    public async Task Contest_Closes_Gate_And_Logs()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        await harness.Commands.ContestAsync(companyId, documentId, Guid.NewGuid(), "Opérateur de test");

        var dto = await harness.AcceptanceQueries.GetAcceptance(companyId, documentId);
        dto!.State.Should().Be(nameof(SelfBilledAcceptanceState.Contested));
        dto.IsAccepted.Should().BeFalse("un document contesté ne doit pas être émis.");
    }

    [Fact]
    public async Task Duplicate_Open_For_Same_Document_Throws_Conflict()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        var act = async () => await OpenPendingAsync(harness, companyId, documentId, Deadline);
        await act.Should().ThrowAsync<ConflictException>(
            "une tentative non terminale existe déjà pour ce document/purpose (index unique partiel — INV-APPROVAL-5).");
    }

    [Fact]
    public async Task Log_Is_Append_Only_Update_And_Delete_Rejected()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        using var conn = await harness.ConnectionFactory.OpenAsync();

        var update = async () => await conn.ExecuteAsync(
            "UPDATE documentapproval.document_approval_log SET operator_name = 'falsifié' WHERE company_id = @c",
            new { c = companyId });
        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM documentapproval.document_approval_log WHERE company_id = @c",
            new { c = companyId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
        await delete.Should().ThrowAsync<PostgresException>();

        (await LogCountAsync(harness, companyId)).Should().Be(1, "ni l'UPDATE ni le DELETE n'ont abouti.");
    }

    [Fact]
    public async Task Log_Truncate_Is_Rejected()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, Deadline);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var truncate = async () => await conn.ExecuteAsync("TRUNCATE documentapproval.document_approval_log");

        await truncate.Should().ThrowAsync<PostgresException>();
        (await LogCountAsync(harness, companyId)).Should().Be(1, "le TRUNCATE a été rejeté.");
    }

    [Fact]
    public async Task Tenant_Isolation_Transition_Does_Not_Touch_Other_Tenant()
    {
        var harness = new MandatsHarness(_fixture);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var documentA = Guid.NewGuid();
        var documentB = Guid.NewGuid();
        await OpenPendingAsync(harness, companyA, documentA, Deadline);
        await OpenPendingAsync(harness, companyB, documentB, Deadline);

        await harness.Commands.AcceptExpresslyAsync(companyA, documentA, Guid.NewGuid(), "Opérateur A");

        (await harness.AcceptanceQueries.GetAcceptance(companyA, documentA))!.State
            .Should().Be(nameof(SelfBilledAcceptanceState.Accepted));
        (await harness.AcceptanceQueries.GetAcceptance(companyB, documentB))!.State
            .Should().Be(nameof(SelfBilledAcceptanceState.PendingAcceptance), "la transition sur A ne touche pas B (CLAUDE.md n°9).");

        // B n'a que sa genèse ; la transition de A n'a écrit aucune ligne dans le journal de B.
        (await LogCountAsync(harness, companyB)).Should().Be(1);
        var logB = await harness.AcceptanceQueries.GetAcceptanceLog(companyB, documentB);
        logB.Should().NotContain(e => e.ToState == nameof(SelfBilledAcceptanceState.Accepted),
            "aucune transition de A n'apparaît dans le journal de B.");
    }

    [Fact]
    public async Task Open_And_Get_RoundTrips_Null_Deadline()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await OpenPendingAsync(harness, companyId, documentId, deadline: null);

        var dto = await harness.AcceptanceQueries.GetAcceptance(companyId, documentId);
        dto.Should().NotBeNull();
        dto!.State.Should().Be(nameof(SelfBilledAcceptanceState.PendingAcceptance));
        dto.DeadlineUtc.Should().BeNull(
            "une échéance NULL (timestamptz) doit faire un round-trip exact : sans délai renseigné, la bascule tacite est impossible (F15 §2.3 / INV-ACCEPT-3).");
    }

    private static Task OpenPendingAsync(
        MandatsHarness harness, Guid companyId, Guid documentId, DateTimeOffset? deadline)
        => harness.Commands.OpenPendingAsync(
            companyId, documentId, PendingSince, deadline, operatorId: null, "Ingestion (test)");

    private static async Task<int> LogCountAsync(MandatsHarness harness, Guid companyId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM documentapproval.document_approval_log WHERE company_id = @c AND validation_purpose = 0",
            new { c = companyId });
    }
}
