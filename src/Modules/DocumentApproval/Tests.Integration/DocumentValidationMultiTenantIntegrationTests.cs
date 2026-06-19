namespace Liakont.Modules.DocumentApproval.Tests.Integration;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.DocumentApproval.Tests.Integration.Fixtures;
using Liakont.Modules.Signature.Contracts;
using Xunit;

/// <summary>
/// Isolation cross-BASE (CLAUDE.md n°9) sur DEUX bases tenant réelles (Testcontainers) : une validation écrite
/// dans la base du tenant A n'existe jamais dans la base du tenant B, et une transition sur A ne touche pas B.
/// </summary>
public sealed class DocumentValidationMultiTenantIntegrationTests : IAsyncLifetime
{
    private const ValidationPurpose Purpose = ValidationPurpose.SelfBilledAcceptance;

    private readonly DocumentApprovalMultiTenantFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_Validation_In_One_Tenant_Base_Is_Invisible_In_The_Other_Base()
    {
        var harnessA = new DocumentApprovalHarness(
            _fixture.CreateConnectionFactory(DocumentApprovalMultiTenantFixture.TenantA));
        var harnessB = new DocumentApprovalHarness(
            _fixture.CreateConnectionFactory(DocumentApprovalMultiTenantFixture.TenantB));

        // Même company_id ET même document_id de part et d'autre : seule la SÉPARATION DE BASE garantit
        // l'isolation (et non un filtre company_id qui pourrait être identique).
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();

        await InsertPendingAsync(harnessA, company, document);
        await TransitionAsync(harnessA, company, document, v => v.Validate(SignatureLevel.Recorded));

        (await harnessA.Queries.GetLatestAttempt(company, document, Purpose))!.State
            .Should().Be(nameof(ValidationState.Validated));
        (await harnessB.Queries.GetLatestAttempt(company, document, Purpose))
            .Should().BeNull("la validation du tenant A n'existe pas dans la base du tenant B (isolation cross-base)");
    }

    private static async Task InsertPendingAsync(DocumentApprovalHarness harness, Guid company, Guid document)
    {
        var validation = DocumentValidation.Create(company, document, Purpose, deadlineUtc: null);
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = DocumentApprovalLogFactory.ForCreation(validation, operatorId: null, "Ingestion (test)");
        await uow.InsertAsync(validation, entry);
        await uow.CommitAsync();
    }

    private static async Task TransitionAsync(
        DocumentApprovalHarness harness, Guid company, Guid document, Action<DocumentValidation> transition)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var loaded = await uow.GetForUpdateAsync(company, document, Purpose, attempt: 1);
        var from = loaded!.State;
        transition(loaded);
        var entry = DocumentApprovalLogFactory.ForTransition(loaded, from, Guid.NewGuid(), "Opérateur A");
        await uow.SaveTransitionAsync(loaded, entry);
        await uow.CommitAsync();
    }
}
