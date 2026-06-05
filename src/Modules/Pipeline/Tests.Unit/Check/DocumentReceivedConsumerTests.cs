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
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Liakont.Modules.Validation.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
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
        harness.RunLog.Saved.Should().NotBeNull();
        harness.RunLog.Saved!.RunType.Should().Be(PipelineRunType.Check);
        harness.RunLog.Saved.Trigger.Should().Be(PipelineRunTrigger.Event);
        harness.RunLog.Saved.DocumentsSucceeded.Should().Be(1);
        harness.RunLog.Saved.DocumentsFailed.Should().Be(0);
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
        IReadOnlyList<PaAccountDto>? paAccounts = null)
    {
        var lifecycle = new FakeDocumentLifecycle();
        var runLog = new FakeRunLogStore();
        var validationService = new FakeValidationService(validation);

        var services = new Dictionary<Type, object>
        {
            [typeof(IDocumentQueries)] = new FakeDocumentQueries(document),
            [typeof(ITenantSettingsQueries)] = new FakeTenantSettingsQueries(companyId, paAccounts),
            [typeof(IPayloadStagingStore)] = staging,
            [typeof(ITvaMappingService)] = new FakeTvaMappingService(mapping),
            [typeof(IValidationService)] = validationService,
            [typeof(IDocumentLifecycle)] = lifecycle,
            [typeof(IPipelineRunLogStore)] = runLog,
        };

        var scopeFactory = new FakeTenantScopeFactory(new FakeServiceProvider(services));
        var consumer = new DocumentReceivedConsumer(
            scopeFactory,
            NullLogger<DocumentReceivedConsumer>.Instance,
            new FixedTimeProvider(CheckTestData.Now));

        return new ConsumerHarness(consumer, lifecycle, runLog, validationService);
    }

    private sealed record ConsumerHarness(
        DocumentReceivedConsumer Consumer,
        FakeDocumentLifecycle Lifecycle,
        FakeRunLogStore RunLog,
        FakeValidationService Validation);
}
