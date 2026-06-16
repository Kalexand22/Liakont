namespace Liakont.Modules.DocumentApproval.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.DocumentApproval.Tests.Integration.Fixtures;
using Liakont.Modules.Signature.Contracts;
using Xunit;

/// <summary>
/// Agrégation N-parties par slots (ADR-0028 §8) sur PostgreSQL réel : les slots et leurs niveaux de preuve
/// sont persistés et rechargés fidèlement ; la complétude bascule l'agrégat en Validated ; un slot refusé le
/// termine négativement. Chaque mutation de slot écrit une ligne de journal (INV-APPROVAL-6).
/// </summary>
[Collection("DocumentApprovalIntegration")]
public sealed class DocumentValidationSlotsIntegrationTests
{
    private const ValidationPurpose Purpose = ValidationPurpose.MultiPartySignature;

    private readonly DocumentApprovalDatabaseFixture _fixture;

    public DocumentValidationSlotsIntegrationTests(DocumentApprovalDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task All_Slots_Approved_Completes_The_Aggregate_With_Per_Slot_Levels()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document, "alice", "bob");

        await SlotActionAsync(harness, company, document, v => v.ApproveSlot("alice", SignatureLevel.QES), "alice");
        (await harness.Queries.GetLatestAttempt(company, document, Purpose))!.State
            .Should().Be(nameof(ValidationState.PendingValidation), "un seul slot rempli ne complète pas");

        await SlotActionAsync(harness, company, document, v => v.ApproveSlot("bob", SignatureLevel.SES), "bob");

        var dto = await harness.Queries.GetLatestAttempt(company, document, Purpose);
        dto!.State.Should().Be(nameof(ValidationState.Validated), "tous les slots distincts approuvés ⇒ Validated");
        dto.Slots.Should().HaveCount(2);
        dto.Slots.Single(s => s.SignerId == "alice").ProofLevel.Should().Be(nameof(SignatureLevel.QES));
        dto.Slots.Single(s => s.SignerId == "bob").ProofLevel.Should().Be(nameof(SignatureLevel.SES));
    }

    [Fact]
    public async Task Rejecting_A_Slot_Terminates_The_Aggregate_Negatively()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        await InsertPendingAsync(harness, company, document, "alice", "bob");

        await SlotActionAsync(harness, company, document, v => v.ApproveSlot("alice", SignatureLevel.AES), "alice");
        await SlotActionAsync(harness, company, document, v => v.RejectSlot("bob"), "bob");

        var dto = await harness.Queries.GetLatestAttempt(company, document, Purpose);
        dto!.State.Should().Be(nameof(ValidationState.Rejected), "terminaison négative immédiate (ADR-0028 §8)");
        dto.IsTerminal.Should().BeTrue();
    }

    private static async Task InsertPendingAsync(
        DocumentApprovalHarness harness, Guid company, Guid document, params string[] signerIds)
    {
        var validation = DocumentValidation.Create(company, document, Purpose, deadlineUtc: null, signerIds: signerIds);
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = DocumentApprovalLogFactory.ForCreation(validation, operatorId: null, "Ingestion (test)");
        await uow.InsertAsync(validation, entry);
        await uow.CommitAsync();
    }

    private static async Task SlotActionAsync(
        DocumentApprovalHarness harness, Guid company, Guid document, Action<DocumentValidation> action, string signerId)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var loaded = await uow.GetForUpdateAsync(company, document, Purpose, attempt: 1);
        var from = loaded!.State;
        action(loaded);
        var entry = DocumentApprovalLogFactory.ForTransition(loaded, from, Guid.NewGuid(), "Signataire", signerId);
        await uow.SaveTransitionAsync(loaded, entry);
        await uow.CommitAsync();
    }
}
