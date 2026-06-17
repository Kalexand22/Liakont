namespace Liakont.Modules.Pipeline.Tests.Unit.Check;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Liakont.Modules.Validation.Contracts;
using Xunit;
using static Liakont.Modules.Pipeline.Tests.Unit.Check.CheckTestDoubles;

/// <summary>Chemin de re-vérification (<c>DocumentRecheckService</c>) — garde d'émission self-billed MND03.</summary>
public sealed class DocumentRecheckServiceTests
{
    private static readonly ValidationResult ValidOk = new(Array.Empty<ValidationIssue>());

    /// <summary>
    /// Un document self-billed Blocked dont l'acceptation n'est PAS acquise reste Blocked après recheck : le gate
    /// fermé maintient le blocage (INV-ACCEPT-2 — « bloquer plutôt qu'émettre faux »). La trace d'audit recheck
    /// est inscrite et le motif contient « auto-facture sous mandat ».
    /// </summary>
    [Fact]
    public async Task SelfBilled_Blocked_Document_Stays_Blocked_When_Gate_Closed()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            documentId: documentId,
            gate: FakeSelfBilledGate.Blocking("PendingAcceptance"),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk);

        var result = await harness.Service.RecheckAsync(documentId, "op-1", "Opérateur Test");

        result.Outcome.Should().Be(DocumentRecheckOutcome.StillBlocked);
        result.BlockingReason.Should().Contain("auto-facture sous mandat");
        harness.Lifecycle.RecheckStillBlockedId.Should().Be(documentId);
        harness.Lifecycle.RecheckReadyToSendId.Should().BeNull("MarkReadyToSendByRecheck ne doit pas être appelé");
    }

    /// <summary>
    /// Chemin de déblocage post-acceptation (INV-ACCEPT-2, MODULE.md MND03) : un document self-billed Blocked dont
    /// l'acceptation est acquise passe à ReadyToSend lors du recheck — c'est le chemin « Blocked → ReadyToSend »
    /// déclenché par le gate ouvert (garde réinterrogée à chaque re-vérification, source UNIQUE de la décision).
    /// </summary>
    [Fact]
    public async Task SelfBilled_Blocked_Document_Reopens_To_ReadyToSend_When_Gate_Open()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            documentId: documentId,
            gate: FakeSelfBilledGate.Allowing(),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk);

        var result = await harness.Service.RecheckAsync(documentId, "op-1", "Opérateur Test");

        result.Outcome.Should().Be(DocumentRecheckOutcome.ReadyToSend);
        harness.Lifecycle.RecheckReadyToSendId.Should().Be(documentId, "acceptation acquise ⇒ Blocked → ReadyToSend");
        harness.Lifecycle.RecheckStillBlockedId.Should().BeNull("RecordRecheckStillBlocked ne doit pas être appelé");
    }

    [Fact]
    public async Task Recheck_Fills_Emitter_From_Tenant_Profile_So_A_Supplierless_Pivot_Becomes_ReadyToSend()
    {
        // RB9 : l'agent ne porte plus l'émetteur ; la plateforme le remplit au READ-TIME depuis le profil tenant.
        // Un pivot stagé SANS émetteur ni nature d'opération + profil/fiscal renseignés → enrichissement au CHECK →
        // document émissible. Preuve que le CHECK appelle bien l'enrichisseur (sinon supplier/operationCategory
        // resteraient nuls et le document serait bloqué).
        var documentId = Guid.NewGuid();
        var harness = Build(
            documentId: documentId,
            gate: FakeSelfBilledGate.Allowing(),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk,
            pivot: CheckTestData.EmitterlessPivot(),
            profile: CheckTestData.EmitterProfile(),
            fiscal: CheckTestData.FiscalSettingsOf("LivraisonBiens"));

        var result = await harness.Service.RecheckAsync(documentId, "op-1", "Opérateur Test");

        result.Outcome.Should().Be(DocumentRecheckOutcome.ReadyToSend,
            "émetteur + nature d'opération remplis au read-time depuis le profil tenant (RB9)");
        harness.Lifecycle.RecheckReadyToSendId.Should().Be(documentId);
    }

    [Fact]
    public async Task Recheck_Blocks_When_Operation_Category_Is_Not_Configured()
    {
        // CLAUDE.md n°2/3 : la nature d'opération n'est JAMAIS devinée. Profil émetteur présent (SIREN rempli) mais
        // paramétrage fiscal absent → operationCategory reste nulle → bloqué (jamais émis faux).
        var documentId = Guid.NewGuid();
        var harness = Build(
            documentId: documentId,
            gate: FakeSelfBilledGate.Allowing(),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk,
            pivot: CheckTestData.EmitterlessPivot(),
            profile: CheckTestData.EmitterProfile(),
            fiscal: null);

        var result = await harness.Service.RecheckAsync(documentId, "op-1", "Opérateur Test");

        result.Outcome.Should().Be(DocumentRecheckOutcome.StillBlocked,
            "nature d'opération non paramétrée → bloqué, jamais devinée");
        result.BlockingReason.Should().Contain("nature d'opération");
        harness.Lifecycle.RecheckStillBlockedId.Should().Be(documentId);
    }

    private static RecheckHarness Build(
        Guid documentId,
        FakeSelfBilledGate gate,
        DocumentTvaMappingResult mapping,
        ValidationResult validation,
        PivotDocumentDto? pivot = null,
        TenantProfileDto? profile = null,
        FiscalSettingsDto? fiscal = null)
    {
        var lifecycle = new FakeDocumentLifecycle();
        var snapshots = new FakeVentilationSnapshotStore();
        var companyId = Guid.NewGuid();
        var canonicalJson = CanonicalJson.Serialize(pivot ?? CheckTestData.SelfBilledSingleLinePivot());

        var services = new Dictionary<Type, object>
        {
            [typeof(IDocumentQueries)] = new FakeDocumentQueries(CheckTestData.Document(documentId, "Blocked")),
            [typeof(ITenantSettingsQueries)] = new FakeTenantSettingsQueries(companyId, profile: profile, fiscal: fiscal),
            [typeof(IPayloadStagingStore)] = FakeStagingStore.Returning(canonicalJson),
            [typeof(ITvaMappingService)] = new FakeTvaMappingService(mapping),
            [typeof(IValidationService)] = new FakeValidationService(validation),
            [typeof(IDocumentLifecycle)] = lifecycle,
            [typeof(IVentilationSnapshotStore)] = snapshots,
            [typeof(ISelfBilledGate)] = gate,
        };

        var tenantContext = new FakeTenantContext(CheckTestData.TenantSlug);
        var service = new DocumentRecheckService(
            new FakeServiceProvider(services),
            tenantContext,
            new FixedTimeProvider(CheckTestData.Now));

        return new RecheckHarness(service, lifecycle, snapshots, gate);
    }

    private sealed record RecheckHarness(
        DocumentRecheckService Service,
        FakeDocumentLifecycle Lifecycle,
        FakeVentilationSnapshotStore Snapshots,
        FakeSelfBilledGate Gate);
}
