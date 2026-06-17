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
/// Câblage de bout en bout de la Règle de gate (ADR-0028 §5, INV-APPROVAL-4 ; SIG06) sur de VRAIES bases tenant
/// (Testcontainers, ≥ 2 bases) : gate ouvert/fermé selon état × niveau de preuve requis (paramétrage tenant V005)
/// × forme, ET isolation cross-base du paramétrage. Le niveau requis est un CHOIX du tenant — un tenant en
/// Recorded n'est jamais bloqué du seul fait de l'absence de fournisseur (CLAUDE.md n°2/3).
/// </summary>
public sealed class DocumentApprovalGateIntegrationTests : IAsyncLifetime
{
    private const ValidationPurpose Purpose = ValidationPurpose.SelfBilledAcceptance;

    private readonly DocumentApprovalMultiTenantFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Default_Requirement_Is_Recorded_So_A_Recorded_Acceptance_Opens_The_Gate_On_Both_Bases()
    {
        foreach (var tenant in new[] { DocumentApprovalMultiTenantFixture.TenantA, DocumentApprovalMultiTenantFixture.TenantB })
        {
            var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory(tenant));
            var company = Guid.NewGuid();
            var document = Guid.NewGuid();

            // Aucune exigence configurée : défaut Recorded. Une acceptation enregistrée ouvre le gate SANS
            // fournisseur de signature (tenant Recorded jamais bloqué — INV-APPROVAL-4).
            (await harness.Requirements.GetRequiredLevelAsync(company, Purpose))
                .Should().Be(nameof(SignatureLevel.Recorded), "le défaut est Recorded (aucune exigence configurée)");

            await InsertPendingAsync(harness, company, document);
            await ValidateRecordedAsync(harness, company, document);

            var decision = await harness.Gate.EvaluateAsync(company, document, Purpose);
            decision.IsOpen.Should().BeTrue($"un Recorded satisfait l'exigence par défaut (base {tenant}) : {decision.Reason}");
        }
    }

    [Fact]
    public async Task A_Tenant_Requiring_AES_Closes_The_Gate_For_A_Bare_Recorded_Without_Affecting_The_Other_Base()
    {
        var harnessA = new DocumentApprovalHarness(_fixture.CreateConnectionFactory(DocumentApprovalMultiTenantFixture.TenantA));
        var harnessB = new DocumentApprovalHarness(_fixture.CreateConnectionFactory(DocumentApprovalMultiTenantFixture.TenantB));

        var company = Guid.NewGuid();
        var document = Guid.NewGuid();

        // Même acceptation Recorded de part et d'autre…
        foreach (var harness in new[] { harnessA, harnessB })
        {
            await InsertPendingAsync(harness, company, document);
            await ValidateRecordedAsync(harness, company, document);
        }

        // …mais SEULE la base A exige AES (paramétrage tenant). L'exigence est isolée par base (database-per-tenant).
        await harnessA.Requirements.SetRequiredLevelAsync(company, Purpose, nameof(SignatureLevel.AES));

        (await harnessA.Gate.EvaluateAsync(company, document, Purpose)).IsOpen
            .Should().BeFalse("un Recorded nu ne franchit pas une exigence AES (ADR-0028 §5 cond. 2)");
        (await harnessB.Gate.EvaluateAsync(company, document, Purpose)).IsOpen
            .Should().BeTrue("la base B n'a pas l'exigence AES (isolation cross-base du paramétrage)");

        (await harnessB.Requirements.GetRequiredLevelAsync(company, Purpose))
            .Should().Be(nameof(SignatureLevel.Recorded), "l'exigence posée sur A n'existe pas sur B");
    }

    [Fact]
    public async Task A_Pending_Or_Absent_Validation_Keeps_The_Gate_Closed_FailClosed()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory(DocumentApprovalMultiTenantFixture.TenantA));
        var company = Guid.NewGuid();
        var pendingDoc = Guid.NewGuid();
        var unknownDoc = Guid.NewGuid();

        await InsertPendingAsync(harness, company, pendingDoc);

        (await harness.Gate.EvaluateAsync(company, pendingDoc, Purpose)).IsOpen
            .Should().BeFalse("PendingValidation n'ouvre pas le gate (état nécessaire non atteint)");
        (await harness.Gate.EvaluateAsync(company, unknownDoc, Purpose)).IsOpen
            .Should().BeFalse("aucune validation enregistrée ⇒ gate fermé (fail-closed)");
    }

    [Fact]
    public async Task SetRequiredLevel_Round_Trips_And_Rejects_An_Invalid_Level()
    {
        var harness = new DocumentApprovalHarness(_fixture.CreateConnectionFactory(DocumentApprovalMultiTenantFixture.TenantB));
        var company = Guid.NewGuid();

        await harness.Requirements.SetRequiredLevelAsync(company, ValidationPurpose.MandateSignature, nameof(SignatureLevel.QES));
        (await harness.Requirements.GetRequiredLevelAsync(company, ValidationPurpose.MandateSignature))
            .Should().Be(nameof(SignatureLevel.QES), "upsert puis lecture du niveau requis configuré");

        var act = () => harness.Requirements.SetRequiredLevelAsync(company, ValidationPurpose.MandateSignature, "None");
        await act.Should().ThrowAsync<ArgumentException>("None n'est pas un niveau requis applicable (le défaut « pas d'exigence » est Recorded)");
    }

    private static async Task InsertPendingAsync(DocumentApprovalHarness harness, Guid company, Guid document)
    {
        var validation = DocumentValidation.Create(company, document, Purpose, deadlineUtc: null);
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = DocumentApprovalLogFactory.ForCreation(validation, operatorId: null, "Ingestion (test)");
        await uow.InsertAsync(validation, entry);
        await uow.CommitAsync();
    }

    private static async Task ValidateRecordedAsync(DocumentApprovalHarness harness, Guid company, Guid document)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var loaded = await uow.GetForUpdateAsync(company, document, Purpose, attempt: 1);
        var from = loaded!.State;
        loaded.Validate(SignatureLevel.Recorded);
        var entry = DocumentApprovalLogFactory.ForTransition(loaded, from, Guid.NewGuid(), "Opérateur");
        await uow.SaveTransitionAsync(loaded, entry);
        await uow.CommitAsync();
    }
}
