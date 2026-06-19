namespace Liakont.Modules.DocumentApproval.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.DocumentApproval.Tests.Integration.Fixtures;
using Liakont.Modules.Signature.Contracts;
using Npgsql;
using Xunit;

/// <summary>
/// Workflow de validation (ADR-0028) sur PostgreSQL réel (Testcontainers) : round-trip de l'état
/// (INV-APPROVAL-2), journalisation atomique « pas de transition sans ligne de journal » dans la même
/// transaction (INV-APPROVAL-6), journal append-only (UPDATE/DELETE/TRUNCATE rejetés par trigger base),
/// atomicité (transaction abandonnée ⇒ rien), isolation par société dans une même base (CLAUDE.md n°9).
/// </summary>
[Collection("DocumentApprovalIntegration")]
public sealed class DocumentValidationIntegrationTests
{
    private const ValidationPurpose Purpose = ValidationPurpose.SelfBilledAcceptance;
    private static readonly DateTimeOffset Deadline = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    private readonly DocumentApprovalDatabaseFixture _fixture;

    public DocumentValidationIntegrationTests(DocumentApprovalDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_RoundTrips_Pending_And_Writes_Genesis_Log()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document);

        var dto = await harness.Queries.GetLatestAttempt(company, document, Purpose);
        dto.Should().NotBeNull();
        dto!.State.Should().Be(nameof(ValidationState.PendingValidation));
        dto.Attempt.Should().Be(1);
        dto.IsTerminal.Should().BeFalse();

        var log = await harness.Queries.GetApprovalLog(company, document, Purpose);
        log.Should().HaveCount(1, "la création écrit une ligne de genèse (INV-APPROVAL-6)");
        log[0].FromState.Should().BeNull("la genèse n'a pas d'état « avant »");
        log[0].ToState.Should().Be(nameof(ValidationState.PendingValidation));
    }

    [Fact]
    public async Task Express_Validation_Persists_State_And_Logs_Transition_In_Same_Transaction()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document);

        await TransitionAsync(harness, company, document, attempt: 1,
            v => v.Validate(SignatureLevel.Recorded), operatorId, "Opérateur de test");

        var dto = await harness.Queries.GetLatestAttempt(company, document, Purpose);
        dto!.State.Should().Be(nameof(ValidationState.Validated));
        dto.ProofLevel.Should().Be(nameof(SignatureLevel.Recorded));
        dto.ExpressAcceptanceRecorded.Should().BeTrue();

        var log = await harness.Queries.GetApprovalLog(company, document, Purpose);
        log.Should().HaveCount(2);
        log[0].FromState.Should().Be(nameof(ValidationState.PendingValidation));
        log[0].ToState.Should().Be(nameof(ValidationState.Validated));
        log[0].OperatorId.Should().Be(operatorId);
    }

    [Fact]
    public async Task Log_Is_Append_Only_Update_Delete_And_Truncate_Rejected()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document);

        using var conn = await harness.ConnectionFactory.OpenAsync();

        var update = async () => await conn.ExecuteAsync(
            "UPDATE documentapproval.document_approval_log SET operator_name = 'falsifié' WHERE company_id = @c",
            new { c = company });
        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM documentapproval.document_approval_log WHERE company_id = @c", new { c = company });
        var truncate = async () => await conn.ExecuteAsync("TRUNCATE documentapproval.document_approval_log");

        (await update.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");
        await delete.Should().ThrowAsync<PostgresException>();
        await truncate.Should().ThrowAsync<PostgresException>();

        (await LogCountAsync(harness, company)).Should().Be(1, "ni UPDATE ni DELETE ni TRUNCATE n'ont abouti");
    }

    [Fact]
    public async Task Transition_And_Log_Are_Atomic_Abandoned_Transaction_Persists_Nothing()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document);
        var logBefore = await LogCountAsync(harness, company);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var loaded = await uow.GetForUpdateAsync(company, document, Purpose, attempt: 1);
            var from = loaded!.State;
            loaded.Validate(SignatureLevel.Recorded);
            var entry = DocumentApprovalLogFactory.ForTransition(loaded, from, Guid.NewGuid(), "Opérateur de test");
            await uow.SaveTransitionAsync(loaded, entry);

            // Pas de CommitAsync : la sortie du bloc déclenche le rollback (TransactionScope.DisposeAsync).
        }

        var dto = await harness.Queries.GetLatestAttempt(company, document, Purpose);
        dto!.State.Should().Be(nameof(ValidationState.PendingValidation), "la transition non validée a été annulée");
        (await LogCountAsync(harness, company)).Should().Be(logBefore, "l'entrée de journal a été annulée avec la transition");
    }

    [Fact]
    public async Task Transition_On_One_Company_Does_Not_Touch_Another_Company()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var documentA = Guid.NewGuid();
        var documentB = Guid.NewGuid();
        await InsertPendingAsync(harness, companyA, documentA);
        await InsertPendingAsync(harness, companyB, documentB);

        await TransitionAsync(harness, companyA, documentA, attempt: 1,
            v => v.Validate(SignatureLevel.Recorded), Guid.NewGuid(), "Opérateur A");

        (await harness.Queries.GetLatestAttempt(companyA, documentA, Purpose))!.State
            .Should().Be(nameof(ValidationState.Validated));
        (await harness.Queries.GetLatestAttempt(companyB, documentB, Purpose))!.State
            .Should().Be(nameof(ValidationState.PendingValidation), "la transition sur A ne touche pas B (CLAUDE.md n°9)");
        (await LogCountAsync(harness, companyB)).Should().Be(1);
    }

    private static async Task InsertPendingAsync(DocumentApprovalHarness harness, Guid company, Guid document)
    {
        var validation = DocumentValidation.Create(company, document, Purpose, Deadline);
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = DocumentApprovalLogFactory.ForCreation(validation, operatorId: null, "Ingestion (test)");
        await uow.InsertAsync(validation, entry);
        await uow.CommitAsync();
    }

    private static async Task TransitionAsync(
        DocumentApprovalHarness harness, Guid company, Guid document, int attempt,
        Action<DocumentValidation> transition, Guid? operatorId, string? operatorName)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var loaded = await uow.GetForUpdateAsync(company, document, Purpose, attempt);
        var from = loaded!.State;
        transition(loaded);
        var entry = DocumentApprovalLogFactory.ForTransition(loaded, from, operatorId, operatorName);
        await uow.SaveTransitionAsync(loaded, entry);
        await uow.CommitAsync();
    }

    private static async Task<int> LogCountAsync(DocumentApprovalHarness harness, Guid company)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM documentapproval.document_approval_log WHERE company_id = @c",
            new { c = company });
    }
}
