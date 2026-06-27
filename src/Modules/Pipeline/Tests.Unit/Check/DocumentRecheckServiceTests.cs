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

    /// <summary>
    /// Re-vérification d'un document REJETÉ par la PA dont la cause est corrigée : il repart ReadyToSend (le rejet
    /// n'est plus un cul-de-sac). La transition RejectedByPa → ReadyToSend passe par la même branche « prêt » que
    /// le déblocage d'un Blocked (MarkReadyToSendByRecheck), aucun re-blocage.
    /// </summary>
    [Fact]
    public async Task RejectedByPa_Document_Reevaluated_Ready_Becomes_ReadyToSend()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            documentId: documentId,
            gate: FakeSelfBilledGate.Allowing(),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk,
            state: "RejectedByPa");

        var result = await harness.Service.RecheckAsync(documentId, "op-1", "Opérateur Test");

        result.Outcome.Should().Be(DocumentRecheckOutcome.ReadyToSend, "cause du rejet corrigée ⇒ RejectedByPa → ReadyToSend");
        harness.Lifecycle.RecheckReadyToSendId.Should().Be(documentId);
        harness.Lifecycle.RecheckBlockedFromRejectedId.Should().BeNull("la branche prêt ne re-bloque pas");
        harness.Lifecycle.RecheckStillBlockedId.Should().BeNull("RecordRecheckStillBlocked ne concerne que les Blocked");
    }

    /// <summary>
    /// Re-vérification d'un document REJETÉ par la PA dont la cause N'EST PAS corrigée : il est TRANSITIONNÉ vers
    /// Blocked (MarkBlockedByRecheck) avec le motif réévalué — il quitte le cul-de-sac pour montrer la cause à
    /// corriger (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3). Surtout PAS RecordRecheckStillBlocked (qui ne
    /// transitionne pas, réservé aux Blocked).
    /// </summary>
    [Fact]
    public async Task RejectedByPa_Document_Reevaluated_Not_Ready_Is_Transitioned_To_Blocked_With_Reason()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            documentId: documentId,
            gate: FakeSelfBilledGate.Allowing(),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk,
            pivot: CheckTestData.EmitterlessPivot(),
            profile: CheckTestData.EmitterProfile(),
            fiscal: null,
            state: "RejectedByPa");

        var result = await harness.Service.RecheckAsync(documentId, "op-1", "Opérateur Test");

        result.Outcome.Should().Be(DocumentRecheckOutcome.StillBlocked, "cause non corrigée ⇒ RejectedByPa → Blocked");
        result.State.Should().Be("Blocked");
        result.BlockingReason.Should().Contain("nature d'opération");
        harness.Lifecycle.RecheckBlockedFromRejectedId.Should().Be(documentId, "la branche pas-prêt d'un rejeté transitionne vers Blocked");
        harness.Lifecycle.RecheckBlockedFromRejectedReason.Should().Contain("nature d'opération");
        harness.Lifecycle.RecheckStillBlockedId.Should().BeNull("RecordRecheckStillBlocked (sans transition) ne s'applique pas à un rejeté");
        harness.Lifecycle.RecheckReadyToSendId.Should().BeNull("le document n'est pas prêt");
    }

    /// <summary>
    /// Non-régression : un document déjà BLOQUÉ réévalué « pas prêt » garde le comportement actuel — aucune
    /// transition (RecordRecheckStillBlocked), JAMAIS la transition d'un rejeté (MarkBlockedByRecheck).
    /// </summary>
    [Fact]
    public async Task Blocked_Document_Reevaluated_Not_Ready_Still_Uses_RecordRecheckStillBlocked()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            documentId: documentId,
            gate: FakeSelfBilledGate.Allowing(),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk,
            pivot: CheckTestData.EmitterlessPivot(),
            profile: CheckTestData.EmitterProfile(),
            fiscal: null,
            state: "Blocked");

        var result = await harness.Service.RecheckAsync(documentId, "op-1", "Opérateur Test");

        result.Outcome.Should().Be(DocumentRecheckOutcome.StillBlocked);
        harness.Lifecycle.RecheckStillBlockedId.Should().Be(documentId, "un Blocked reste sur RecordRecheckStillBlocked (pas de transition)");
        harness.Lifecycle.RecheckBlockedFromRejectedId.Should().BeNull("MarkBlockedByRecheck est réservé à un document rejeté");
    }

    /// <summary>
    /// Re-vérification d'un document REJETÉ par la PA dont le pivot stagé est INDISPONIBLE (purgé/altéré) : aucune
    /// re-vérification n'est possible. Le résultat reporte l'ÉTAT D'ENTRÉE réel (RejectedByPa), JAMAIS « Blocked »
    /// codé en dur, et AUCUNE transition de cycle de vie n'est tentée (le document garde son état).
    /// </summary>
    [Theory]
    [InlineData("Blocked")]
    [InlineData("RejectedByPa")]
    public async Task Content_Unavailable_Reports_The_Entry_State_And_Performs_No_Transition(string state)
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            documentId: documentId,
            gate: FakeSelfBilledGate.Allowing(),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk,
            state: state,
            staging: FakeStagingStore.Throwing(
                StagedPayloadNotFoundException.ForKey(new StagedPayloadKey(CheckTestData.TenantSlug, documentId, "hash-0007"))));

        var result = await harness.Service.RecheckAsync(documentId, "op-1", "Opérateur Test");

        result.Outcome.Should().Be(DocumentRecheckOutcome.ContentUnavailable);
        result.State.Should().Be(state, "l'état d'entrée est reporté tel quel, jamais « Blocked » en dur");

        // Aucune transition de cycle de vie : le document garde son état (pas de re-vérification possible).
        harness.Lifecycle.RecheckReadyToSendId.Should().BeNull();
        harness.Lifecycle.RecheckStillBlockedId.Should().BeNull();
        harness.Lifecycle.RecheckBlockedFromRejectedId.Should().BeNull();
    }

    private static RecheckHarness Build(
        Guid documentId,
        FakeSelfBilledGate gate,
        DocumentTvaMappingResult mapping,
        ValidationResult validation,
        PivotDocumentDto? pivot = null,
        TenantProfileDto? profile = null,
        FiscalSettingsDto? fiscal = null,
        string state = "Blocked",
        IPayloadStagingStore? staging = null)
    {
        var lifecycle = new FakeDocumentLifecycle();
        var snapshots = new FakeVentilationSnapshotStore();
        var marginRegistry = new FakeMarginRegistryStore();
        var companyId = Guid.NewGuid();
        var canonicalJson = CanonicalJson.Serialize(pivot ?? CheckTestData.SelfBilledSingleLinePivot());

        var services = new Dictionary<Type, object>
        {
            [typeof(IDocumentQueries)] = new FakeDocumentQueries(CheckTestData.Document(documentId, state)),
            [typeof(ITenantSettingsQueries)] = new FakeTenantSettingsQueries(companyId, profile: profile, fiscal: fiscal),
            [typeof(IPayloadStagingStore)] = staging ?? FakeStagingStore.Returning(canonicalJson),
            [typeof(ITvaMappingService)] = new FakeTvaMappingService(mapping),
            [typeof(IValidationService)] = new FakeValidationService(validation),
            [typeof(IDocumentLifecycle)] = lifecycle,
            [typeof(IVentilationSnapshotStore)] = snapshots,
            [typeof(IMarginRegistryStore)] = marginRegistry,
            [typeof(ISelfBilledGate)] = gate,
        };

        var tenantContext = new FakeTenantContext(CheckTestData.TenantSlug);
        var service = new DocumentRecheckService(
            new FakeServiceProvider(services),
            tenantContext,
            new FixedTimeProvider(CheckTestData.Now));

        return new RecheckHarness(service, lifecycle, snapshots, marginRegistry, gate);
    }

    private sealed record RecheckHarness(
        DocumentRecheckService Service,
        FakeDocumentLifecycle Lifecycle,
        FakeVentilationSnapshotStore Snapshots,
        FakeMarginRegistryStore MarginRegistry,
        FakeSelfBilledGate Gate);
}
