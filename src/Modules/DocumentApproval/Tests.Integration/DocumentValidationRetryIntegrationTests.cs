namespace Liakont.Modules.DocumentApproval.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.DocumentApproval.Tests.Integration.Fixtures;
using Liakont.Modules.Signature.Contracts;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Ré-essai par nouvel <c>attempt</c> (ADR-0028 §6, INV-APPROVAL-5) sur PostgreSQL réel : index unique partiel
/// (≤ 1 tentative non terminale), garde anti-race (l'attempt N doit être un échec terminal — test de
/// concurrence), et exclusion du self-billing. Purpose signature : <c>MandateSignature</c>.
/// </summary>
[Collection("DocumentApprovalIntegration")]
public sealed class DocumentValidationRetryIntegrationTests
{
    private const ValidationPurpose Purpose = ValidationPurpose.MandateSignature;

    private readonly DocumentApprovalDatabaseFixture _fixture;

    public DocumentValidationRetryIntegrationTests(DocumentApprovalDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Second_Non_Terminal_Attempt_Is_Rejected_By_The_Partial_Unique_Index()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document, attempt: 1);

        var second = DocumentValidation.Create(company, document, Purpose, deadlineUtc: null, attempt: 2);
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = DocumentApprovalLogFactory.ForCreation(second, operatorId: null, "test");
        var act = async () =>
        {
            await uow.InsertAsync(second, entry);
            await uow.CommitAsync();
        };

        await act.Should().ThrowAsync<ConflictException>(
            "au plus UNE tentative non terminale (index unique partiel, INV-APPROVAL-5)");
    }

    [Fact]
    public async Task CreateNextAttempt_Is_Refused_While_The_Latest_Attempt_Is_Not_A_Terminal_Failure()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document, attempt: 1);

        await using var uow = await harness.UowFactory.BeginAsync();
        var act = async () => await uow.CreateNextAttemptAsync(
            company, document, Purpose, deadlineUtc: null, signerIds: null,
            operatorId: null, operatorName: "test");

        await act.Should().ThrowAsync<InvalidOperationException>(
            "la tentative N (Pending) n'est pas un échec terminal — un succès concurrent ne doit pas être masqué (garde anti-race)");
    }

    [Fact]
    public async Task CreateNextAttempt_Succeeds_After_A_Terminal_Failure()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document, attempt: 1);
        await RejectAsync(harness, company, document, attempt: 1);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var next = await uow.CreateNextAttemptAsync(
                company, document, Purpose, deadlineUtc: null, signerIds: null,
                operatorId: Guid.NewGuid(), operatorName: "test");
            await uow.CommitAsync();
            next.Attempt.Should().Be(2);
        }

        var dto = await harness.Queries.GetLatestAttempt(company, document, Purpose);
        dto!.Attempt.Should().Be(2);
        dto.State.Should().Be(nameof(ValidationState.PendingValidation));
    }

    [Fact]
    public async Task Concurrent_CreateNextAttempt_Produces_Exactly_One_New_Attempt()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document, attempt: 1);
        await RejectAsync(harness, company, document, attempt: 1);

        // Deux créations CONCURRENTES de l'attempt N+1 sur une tentative N échouée : la garde anti-race
        // (FOR UPDATE) + l'index unique partiel garantissent qu'EXACTEMENT une réussit.
        var results = await Task.WhenAll(
            TryCreateNextAttemptAsync(harness, company, document),
            TryCreateNextAttemptAsync(harness, company, document));

        results.Count(success => success).Should().Be(1, "exactement une création concurrente aboutit");

        var attemptCount = await AttemptCountAsync(harness, company, document);
        attemptCount.Should().Be(2, "attempt 1 (Rejected) + une seule nouvelle tentative");
        var activeCount = await ActiveAttemptCountAsync(harness, company, document);
        activeCount.Should().Be(1, "au plus une tentative non terminale (index unique partiel)");
    }

    private static async Task<bool> TryCreateNextAttemptAsync(
        DocumentApprovalHarness harness, Guid company, Guid document)
    {
        try
        {
            await using var uow = await harness.UowFactory.BeginAsync();
            await uow.CreateNextAttemptAsync(
                company, document, Purpose, deadlineUtc: null, signerIds: null,
                operatorId: null, operatorName: "test");
            await uow.CommitAsync();
            return true;
        }
        catch (Exception ex) when (ex is ConflictException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task InsertPendingAsync(DocumentApprovalHarness harness, Guid company, Guid document, int attempt)
    {
        var validation = DocumentValidation.Create(company, document, Purpose, deadlineUtc: null, attempt: attempt);
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = DocumentApprovalLogFactory.ForCreation(validation, operatorId: null, "Ingestion (test)");
        await uow.InsertAsync(validation, entry);
        await uow.CommitAsync();
    }

    private static async Task RejectAsync(DocumentApprovalHarness harness, Guid company, Guid document, int attempt)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var loaded = await uow.GetForUpdateAsync(company, document, Purpose, attempt);
        var from = loaded!.State;
        loaded.Reject();
        var entry = DocumentApprovalLogFactory.ForTransition(loaded, from, Guid.NewGuid(), "Opérateur de test");
        await uow.SaveTransitionAsync(loaded, entry);
        await uow.CommitAsync();
    }

    private static async Task<int> AttemptCountAsync(DocumentApprovalHarness harness, Guid company, Guid document)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM documentapproval.document_validations WHERE company_id = @c AND document_id = @d AND validation_purpose = @p",
            new { c = company, d = document, p = (int)Purpose });
    }

    private static async Task<int> ActiveAttemptCountAsync(DocumentApprovalHarness harness, Guid company, Guid document)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM documentapproval.document_validations WHERE company_id = @c AND document_id = @d AND validation_purpose = @p AND state IN (0, 1)",
            new { c = company, d = document, p = (int)Purpose });
    }
}
