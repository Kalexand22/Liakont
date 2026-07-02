namespace Liakont.Modules.Pipeline.Tests.Unit.Check;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Reference.Contracts;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Liakont.Modules.Validation.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;
using static Liakont.Modules.Pipeline.Tests.Unit.Check.CheckTestDoubles;

/// <summary>Orchestration du CHECK (INV-PIPELINE-008/009/010/011/012).</summary>
public sealed class DocumentReceivedConsumerTests
{
    private static readonly ValidationResult ValidOk = new(Array.Empty<ValidationIssue>());

    [Fact]
    public async Task Mapped_And_Valid_Marks_ReadyToSend_And_Writes_RunLog()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.ReadyToSendId.Should().Be(documentId);
        harness.Lifecycle.ReadyToSendMappingVersion.Should().Be("cmp-v1");
        harness.Lifecycle.BlockedId.Should().BeNull();

        // ADR-0015 : la ventilation sourcée est capturée au passage ReadyToSend (snapshot requêtable).
        harness.Snapshots.Saved.Should().NotBeNull("le snapshot de ventilation est écrit au CHECK (ADR-0015).");
        harness.Snapshots.Saved!.MappingVersion.Should().Be("cmp-v1");
        harness.Snapshots.Saved.Lines.Should().NotBeEmpty();

        harness.RunLog.Saved.Should().NotBeNull();
        harness.RunLog.Saved!.RunType.Should().Be(PipelineRunType.Check);
        harness.RunLog.Saved.Trigger.Should().Be(PipelineRunTrigger.Event);
        harness.RunLog.Saved.DocumentsSucceeded.Should().Be(1);
        harness.RunLog.Saved.DocumentsFailed.Should().Be(0);

        // L2 — un document NON marge (taxable) prêt à l'envoi ne crée jamais d'entrée de registre de marge ;
        // il SUPPRIME une éventuelle entrée périmée (idempotent : la projection suit l'état courant).
        harness.MarginRegistry.Upserted.Should().BeNull();
        harness.MarginRegistry.Deleted.Should().Be(documentId);
    }

    [Fact]
    public async Task Margin_Document_Upserts_The_Margin_Registry_Entry_At_ReadyToSend()
    {
        // L2 — un bordereau au RÉGIME DE LA MARGE prêt à l'envoi écrit son entrée de registre (base HT + TVA sur
        // marge à déclarer), calculée par les cœurs purs PARTAGÉS (ToHt : 10 TTC @ 20 % → 8,33 HT / 1,67 TVA).
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.MarginPivot(buyerFeeTtc: 10.00m)),
            mapping: CheckTestData.MarginMappedResult(feeRate: 20m),
            validation: ValidOk);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.ReadyToSendId.Should().Be(documentId);
        harness.MarginRegistry.Deleted.Should().BeNull();
        harness.MarginRegistry.Upserted.Should().NotBeNull();
        harness.MarginRegistry.Upserted!.DocumentId.Should().Be(documentId);
        harness.MarginRegistry.Upserted.VatRate.Should().Be(20m);
        harness.MarginRegistry.Upserted.MarginBaseHt.Should().Be(8.33m);
        harness.MarginRegistry.Upserted.MarginVat.Should().Be(1.67m);
        harness.MarginRegistry.Upserted.IssueDate.Should().Be(new DateOnly(2026, 6, 26));
        harness.MarginRegistry.Upserted.CurrencyCode.Should().Be("EUR");
    }

    [Fact]
    public async Task Unmapped_Regime_Blocks_And_Writes_RunLog()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot(regimeCode: "INCONNU")),
            mapping: CheckTestData.BlockedLineResult(),
            validation: ValidOk);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("absent de la table de mapping");
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
        harness.RunLog.Saved!.DocumentsFailed.Should().Be(1);
    }

    [Fact]
    public async Task Blocking_Validation_Issue_Blocks_With_Operator_Message()
    {
        var documentId = Guid.NewGuid();
        var blocking = new ValidationResult(new[]
        {
            ValidationIssue.Blocking("DOC_TOTAL_MISMATCH", "Le total du document ne correspond pas à la somme des lignes."),
        });
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: blocking);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("ne correspond pas");
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
    }

    [Fact]
    public async Task SelfBilled_Not_Accepted_Blocks_With_Acceptance_Motif()
    {
        // MND03 (ADR-0024 §3, INV-ACCEPT-2) : contenu valide MAIS acceptation en attente ⇒ maintenu Blocked,
        // jamais ReadyToSend (« bloquer plutôt qu'émettre faux »).
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SelfBilledSingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk,
            selfBilledGate: FakeSelfBilledGate.Blocking("PendingAcceptance"));

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("auto-facture sous mandat");
        harness.Lifecycle.BlockReason.Should().Contain("en attente");
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
        harness.SelfBilledGate.LastDocumentId.Should().Be(documentId, "la garde est interrogée avec l'identifiant du document");
        harness.RunLog.Saved!.DocumentsFailed.Should().Be(1);
    }

    [Fact]
    public async Task SelfBilled_Contested_Blocks_With_Contested_Situation()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SelfBilledSingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk,
            selfBilledGate: FakeSelfBilledGate.Blocking("Contested"));

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("contestée");
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
    }

    [Fact]
    public async Task SelfBilled_Accepted_But_Gate_Closed_Blocks_Without_Claiming_No_Acceptance()
    {
        // SIG06 : le gate générique peut se fermer alors qu'une acceptation EST enregistrée (Accepted) — quand le
        // tenant exige un niveau de preuve supérieur que la validation attachée ne couvre pas. Le motif opérateur
        // ne doit JAMAIS affirmer « aucune acceptation enregistrée » (factuellement faux, CLAUDE.md n°12) ; il doit
        // orienter vers le niveau de preuve requis. Le blocage lui-même reste correct (fail-closed).
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SelfBilledSingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk,
            selfBilledGate: FakeSelfBilledGate.Blocking("Accepted"));

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId, "fail-closed : émission refusée tant que le niveau requis n'est pas atteint");
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
        harness.Lifecycle.BlockReason.Should().NotContain(
            "aucune acceptation", "une acceptation EST enregistrée — le blocage vient du niveau de preuve, pas de son absence");
        harness.Lifecycle.BlockReason.Should().Contain(
            "niveau de preuve requis", "le motif oriente l'opérateur vers le niveau eIDAS exigé par son paramétrage");
    }

    [Fact]
    public async Task SelfBilled_Accepted_Marks_ReadyToSend()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SelfBilledSingleLinePivot()),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk,
            selfBilledGate: FakeSelfBilledGate.Allowing());

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.ReadyToSendId.Should().Be(documentId, "acceptation acquise ⇒ le gate est ouvert");
        harness.Lifecycle.ReadyToSendMappingVersion.Should().Be("cmp-v1");
        harness.Lifecycle.BlockedId.Should().BeNull();
        harness.SelfBilledGate.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Non_SelfBilled_Document_Never_Consults_The_Gate()
    {
        // La garde est strictement gardée par pivot.IsSelfBilled : un document standard ne consulte JAMAIS
        // le gate (non-régression — un gate fermé ne doit pas bloquer un document non concerné).
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk,
            selfBilledGate: FakeSelfBilledGate.Blocking());

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.SelfBilledGate.WasCalled.Should().BeFalse("un document non self-billed ne déclenche pas la garde MND03");
        harness.Lifecycle.ReadyToSendId.Should().Be(documentId);
        harness.Lifecycle.BlockedId.Should().BeNull();
    }

    [Fact]
    public async Task SelfBilled_With_Pa_Without_SelfBilling_Capability_Blocks_With_Capability_Motif()
    {
        // MND07 (F15 §1.2, CLAUDE.md n°3/8) : une auto-facture sous mandat vers une PA active qui NE déclare PAS
        // la capacité d'émission 389 ⇒ bloquée, jamais dégradée en facture standard. La garde de capacité passe
        // AVANT la garde d'acceptation (un 389 inémissible n'a pas à être évalué pour acceptation).
        var documentId = Guid.NewGuid();
        var registry = new FakePaClientRegistry(
            new CapabilityStubPaClient(new PaCapabilities { PaName = "PA Sans 389", SupportsSelfBilling = false }));
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SelfBilledSingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk,
            paAccounts: new[] { CheckTestData.PaAccount("Sandbox", isActive: true) },
            selfBilledGate: FakeSelfBilledGate.Allowing(),
            paRegistry: registry);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("ne déclare pas la capacité d'émission");
        harness.Lifecycle.BlockReason.Should().Contain("389");
        harness.Lifecycle.BlockReason.Should().Contain("PA Sans 389");
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
        harness.SelfBilledGate.WasCalled.Should().BeFalse("la garde de capacité bloque avant la garde d'acceptation");
    }

    [Fact]
    public async Task SelfBilled_With_Capable_Pa_Proceeds_To_The_Acceptance_Gate()
    {
        // MND07 : capacité 389 présente ⇒ la garde de capacité n'over-bloque pas ; l'acceptation tranche ensuite
        // (ici acquise ⇒ ReadyToSend). Prouve que la garde n'est pas un blocage généralisé des self-billed.
        var documentId = Guid.NewGuid();
        var registry = new FakePaClientRegistry(
            new CapabilityStubPaClient(new PaCapabilities { PaName = "PA 389", SupportsSelfBilling = true }));
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SelfBilledSingleLinePivot()),
            mapping: CheckTestData.MappedResult(version: "cmp-v1"),
            validation: ValidOk,
            paAccounts: new[] { CheckTestData.PaAccount("Sandbox", isActive: true) },
            selfBilledGate: FakeSelfBilledGate.Allowing(),
            paRegistry: registry);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.ReadyToSendId.Should().Be(documentId);
        harness.Lifecycle.BlockedId.Should().BeNull();
        harness.SelfBilledGate.WasCalled.Should().BeTrue("capacité OK ⇒ on passe à la garde d'acceptation");
    }

    [Fact]
    public async Task Production_Account_Plus_Unvalidated_Table_Blocks_All_Without_Validating()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(isValidated: false),
            validation: ValidOk,
            paAccounts: new[] { CheckTestData.PaAccount("Production", isActive: true) });

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("Production");
        harness.Validation.WasCalled.Should().BeFalse("la garde-fou production bloque avant la validation");
        harness.Validation.MappingIndependentWasCalled.Should().BeFalse(
            "la garde-fou production reste SÉQUENTIELLE (FIX06, D5) : elle n'agrège aucun autre motif");
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
    }

    [Fact]
    public async Task Staging_Demo_Unvalidated_Table_Without_Production_Account_Maps_Normally()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(isValidated: false),
            validation: ValidOk,
            paAccounts: new[] { CheckTestData.PaAccount("Staging", isActive: true) });

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.ReadyToSendId.Should().Be(documentId);
        harness.Lifecycle.BlockedId.Should().BeNull();
    }

    [Fact]
    public async Task Without_Any_Active_Pa_Account_Sandbox_Test_Identifiers_Are_Not_Tolerated()
    {
        // BUG-23 (durcissement) : la dérogation aux SIREN de TEST sandbox exige une PREUVE POSITIVE de contexte hors
        // production. Aucun compte PA actif ⇒ fail-closed : la validation reçoit allowSandboxTestIdentifiers = false
        // (sinon un SIREN de test resterait toléré sur un tenant non configuré, alors qu'il doit échouer au Luhn).
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Validation.WasCalled.Should().BeTrue();
        harness.Validation.LastAllowSandboxTestIdentifiers.Should().BeFalse(
            "sans compte PA actif, aucune dérogation aux SIREN de test (fail-closed)");
    }

    [Fact]
    public async Task Active_Sandbox_Account_Tolerates_Sandbox_Test_Identifiers()
    {
        // Contexte sandbox réellement câblé (compte PA actif, aucun en production) ⇒ la validation reçoit
        // allowSandboxTestIdentifiers = true : le flux recette SuperPDP reste préservé.
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk,
            paAccounts: new[] { CheckTestData.PaAccount("Sandbox", isActive: true) });

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Validation.WasCalled.Should().BeTrue();
        harness.Validation.LastAllowSandboxTestIdentifiers.Should().BeTrue(
            "un compte sandbox actif (aucun en production) tolère les SIREN de test");
    }

    [Fact]
    public async Task Active_Production_Account_Never_Tolerates_Sandbox_Test_Identifiers()
    {
        // Même avec une table validée (le garde-fou production ne bloque pas), un compte production actif INTERDIT la
        // dérogation : la clé de Luhn reste stricte en production (CLAUDE.md n°3).
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk,
            paAccounts: new[] { CheckTestData.PaAccount("Production", isActive: true) });

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Validation.WasCalled.Should().BeTrue();
        harness.Validation.LastAllowSandboxTestIdentifiers.Should().BeFalse(
            "un compte production actif interdit la dérogation aux SIREN de test");
    }

    [Fact]
    public async Task Absent_Mapping_Table_Blocks_With_Create_Table_Reason()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MissingTableResult(),
            validation: ValidOk);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("Aucune table de mapping", "l'action corrective est « créez la table », pas « validez »");
        harness.Validation.WasCalled.Should().BeFalse("la validation COMPLÈTE (document enrichi) ne s'exécute pas quand le mapping échoue");
        harness.Validation.MappingIndependentWasCalled.Should().BeTrue(
            "FIX06 : on agrège les motifs indépendants du mapping même quand la table est absente");
        harness.RunLog.Saved!.DocumentsFailed.Should().Be(1);
    }

    [Fact]
    public async Task Absent_Table_Aggregates_Mapping_Independent_Reasons_In_Single_Block()
    {
        // FIX06 (D5) : table absente + acheteur « pro » → les DEUX motifs dès le premier CHECK.
        var documentId = Guid.NewGuid();
        var buyerProIssue = new ValidationResult(new[]
        {
            ValidationIssue.Blocking(
                "BUYER_LOOKS_PROFESSIONAL",
                "L'acheteur \"Client SARL\" du document n° F-2026-0007 semble être un professionnel."),
        });
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MissingTableResult(),
            validation: ValidOk,
            mappingIndependentValidation: buyerProIssue);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("Aucune table de mapping", "le motif de mapping reste présent");
        harness.Lifecycle.BlockReason.Should().Contain("semble être un professionnel", "le motif indépendant est agrégé (FIX06)");
        harness.Validation.MappingIndependentWasCalled.Should().BeTrue();
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
    }

    [Fact]
    public async Task Unmapped_Regime_Aggregates_Mapping_Independent_Reasons_In_Single_Block()
    {
        // FIX06 (D5) : régime non couvert + acheteur « pro » → les DEUX motifs dès le premier CHECK.
        var documentId = Guid.NewGuid();
        var buyerProIssue = new ValidationResult(new[]
        {
            ValidationIssue.Blocking(
                "BUYER_LOOKS_PROFESSIONAL",
                "L'acheteur \"Client SARL\" du document n° F-2026-0007 semble être un professionnel."),
        });
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot(regimeCode: "INCONNU")),
            mapping: CheckTestData.BlockedLineResult(),
            validation: ValidOk,
            mappingIndependentValidation: buyerProIssue);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("absent de la table de mapping", "le motif de mapping reste présent");
        harness.Lifecycle.BlockReason.Should().Contain("semble être un professionnel", "le motif indépendant est agrégé (FIX06)");
        harness.Validation.MappingIndependentWasCalled.Should().BeTrue();
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
    }

    [Fact]
    public async Task Corrupted_Staging_Blocks_Document_Instead_Of_DeadLettering()
    {
        var documentId = Guid.NewGuid();
        var key = new StagedPayloadKey(CheckTestData.TenantSlug, documentId, "hash-0007");
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: FakeStagingStore.Throwing(StagedPayloadIntegrityException.HashMismatch(key, "deadbeef")),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.BlockedId.Should().Be(documentId);
        harness.Lifecycle.BlockReason.Should().Contain("intégrité");
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
        harness.RunLog.Saved!.DocumentsFailed.Should().Be(1);
    }

    [Fact]
    public async Task Staging_Not_Yet_Available_Propagates_And_Leaves_Document_Untouched()
    {
        var documentId = Guid.NewGuid();
        var key = new StagedPayloadKey(CheckTestData.TenantSlug, documentId, "hash-0007");
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: Guid.NewGuid(),
            staging: FakeStagingStore.Throwing(StagedPayloadNotFoundException.ForKey(key)),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk);

        var act = () => harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        await act.Should().ThrowAsync<StagedPayloadNotFoundException>();
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
        harness.Lifecycle.BlockedId.Should().BeNull();
        harness.RunLog.Saved.Should().BeNull("une exécution transitoire (retry) n'est pas une exécution clôturée");
    }

    [Fact]
    public async Task Already_Processed_Document_Is_A_NoOp()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "ReadyToSend"),
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk);

        await harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        harness.Lifecycle.ReadyToSendId.Should().BeNull();
        harness.Lifecycle.BlockedId.Should().BeNull();
        harness.RunLog.Saved.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_Document_Throws_For_Retry()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: null,
            companyId: Guid.NewGuid(),
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk);

        var act = () => harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.RunLog.Saved.Should().BeNull();
    }

    [Fact]
    public async Task Tenant_Without_Profile_Throws_For_Retry()
    {
        var documentId = Guid.NewGuid();
        var harness = Build(
            document: CheckTestData.Document(documentId, "Detected"),
            companyId: null,
            staging: Staging(CheckTestData.SingleLinePivot()),
            mapping: CheckTestData.MappedResult(),
            validation: ValidOk);

        var act = () => harness.Consumer.HandleAsync(CheckTestData.Event(documentId));

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Lifecycle.ReadyToSendId.Should().BeNull();
        harness.Lifecycle.BlockedId.Should().BeNull();
        harness.RunLog.Saved.Should().BeNull();
    }

    private static FakeStagingStore Staging(PivotDocumentDto pivot) =>
        FakeStagingStore.Returning(CanonicalJson.Serialize(pivot));

    private static ConsumerHarness Build(
        DocumentDto? document,
        Guid? companyId,
        IPayloadStagingStore staging,
        DocumentTvaMappingResult mapping,
        ValidationResult validation,
        IReadOnlyList<PaAccountDto>? paAccounts = null,
        ValidationResult? mappingIndependentValidation = null,
        FakeSelfBilledGate? selfBilledGate = null,
        IPaClientRegistry? paRegistry = null)
    {
        var lifecycle = new FakeDocumentLifecycle();
        var runLog = new FakeRunLogStore();
        var validationService = new FakeValidationService(validation, mappingIndependentValidation);
        var snapshots = new FakeVentilationSnapshotStore();
        var marginRegistry = new FakeMarginRegistryStore();

        // Gate ouvert par défaut (non-régression : les documents non self-billed ne le consultent jamais —
        // la garde est gardée par pivot.IsSelfBilled). Les tests self-billed fournissent un gate explicite.
        var gate = selfBilledGate ?? FakeSelfBilledGate.Allowing();

        var services = new Dictionary<Type, object>
        {
            [typeof(IDocumentQueries)] = new FakeDocumentQueries(document),
            [typeof(ITenantSettingsQueries)] = new FakeTenantSettingsQueries(companyId, paAccounts),
            [typeof(IPayloadStagingStore)] = staging,
            [typeof(ITvaMappingService)] = new FakeTvaMappingService(mapping),
            [typeof(IValidationService)] = validationService,
            [typeof(IDocumentLifecycle)] = lifecycle,
            [typeof(IPipelineRunLogStore)] = runLog,
            [typeof(IVentilationSnapshotStore)] = snapshots,
            [typeof(IMarginRegistryStore)] = marginRegistry,
            [typeof(ISelfBilledGate)] = gate,
            [typeof(ITenantContext)] = new FakeTenantContext(CheckTestData.TenantSlug),
            [typeof(ICountryAliasReferential)] = new FakeCountryAliasReferential(),
        };

        // La garde de capacité 389 (MND07) ne résout le registre PA QUE pour un document self-billed avec une PA
        // active : les tests qui ne l'exercent pas n'en fournissent pas (résolution fail-open — pas de blocage).
        if (paRegistry is not null)
        {
            services[typeof(IPaClientRegistry)] = paRegistry;
        }

        var scopeFactory = new FakeTenantScopeFactory(new FakeServiceProvider(services));
        var consumer = new DocumentReceivedConsumer(
            scopeFactory,
            NullLogger<DocumentReceivedConsumer>.Instance,
            new FixedTimeProvider(CheckTestData.Now));

        return new ConsumerHarness(consumer, lifecycle, runLog, validationService, snapshots, marginRegistry, gate);
    }

    private sealed record ConsumerHarness(
        DocumentReceivedConsumer Consumer,
        FakeDocumentLifecycle Lifecycle,
        FakeRunLogStore RunLog,
        FakeValidationService Validation,
        FakeVentilationSnapshotStore Snapshots,
        FakeMarginRegistryStore MarginRegistry,
        FakeSelfBilledGate SelfBilledGate);
}
