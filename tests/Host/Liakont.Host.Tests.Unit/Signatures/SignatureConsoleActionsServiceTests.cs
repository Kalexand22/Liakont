namespace Liakont.Host.Tests.Unit.Signatures;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Signatures;
using Liakont.Modules.DocumentApproval.Contracts;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests unitaires du service d'actions de la page console des signatures (SIG10). Vérifie la défense en
/// profondeur (permission <c>liakont.actions</c> exigée même si la page masque les boutons), la résolution
/// tenant (<c>company_id</c> obligatoire), la délégation au port générique <see cref="IDocumentApprovalWorkflow"/>
/// avec l'identité d'opérateur, et la traduction des refus métier (demande déjà en cours, transition impossible)
/// en messages opérateur français — sans toucher à une base ni au module réel.
/// </summary>
public sealed class SignatureConsoleActionsServiceTests
{
    private static readonly Guid CompanyId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OperatorId = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid DocId = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task RequestValidation_Without_Actions_Permission_Is_Refused_And_The_Workflow_Is_Not_Called()
    {
        var workflow = new RecordingWorkflow();
        var service = Build(workflow, canAct: false);

        var result = await service.RequestValidationAsync(DocId, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("liakont.actions");
        workflow.RequestCalls.Should().BeEmpty("la garde de permission précède tout appel au port");
    }

    [Fact]
    public async Task RequestValidation_Without_A_Resolved_Tenant_Is_Refused()
    {
        var workflow = new RecordingWorkflow();
        var service = Build(workflow, canAct: true, tenantResolved: false);

        var result = await service.RequestValidationAsync(DocId, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Tenant non résolu");
        workflow.RequestCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestValidation_Delegates_To_The_Workflow_With_The_Tenant_And_Operator()
    {
        var workflow = new RecordingWorkflow();
        var service = Build(workflow, canAct: true);
        var deadline = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var result = await service.RequestValidationAsync(DocId, ValidationPurpose.MandateSignature, deadline);

        result.Success.Should().BeTrue();
        workflow.RequestCalls.Should().ContainSingle();
        var call = workflow.RequestCalls[0];
        call.CompanyId.Should().Be(CompanyId);
        call.DocumentId.Should().Be(DocId);
        call.Purpose.Should().Be(ValidationPurpose.MandateSignature);
        call.DeadlineUtc.Should().Be(deadline);
        call.OperatorId.Should().Be(OperatorId);
        call.OperatorName.Should().Be("Opérateur Test");
    }

    [Fact]
    public async Task RequestValidation_Maps_A_Conflict_To_An_Operator_Message()
    {
        var workflow = new RecordingWorkflow { RequestThrows = new ConflictException("duplicate") };
        var service = Build(workflow, canAct: true);

        var result = await service.RequestValidationAsync(DocId, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("déjà en cours");
    }

    [Fact]
    public async Task RecordRecorded_Maps_A_Closed_Machine_Refusal_To_An_Operator_Message()
    {
        var workflow = new RecordingWorkflow { TransitionThrows = new InvalidOperationException("no attempt") };
        var service = Build(workflow, canAct: true);

        var result = await service.RecordRecordedAsync(DocId, ValidationPurpose.SelfBilledAcceptance);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Action impossible dans l'état actuel");
    }

    [Fact]
    public async Task RecordRecorded_Delegates_To_The_Workflow_On_Success()
    {
        var workflow = new RecordingWorkflow();
        var service = Build(workflow, canAct: true);

        var result = await service.RecordRecordedAsync(DocId, ValidationPurpose.SelfBilledAcceptance);

        result.Success.Should().BeTrue();
        workflow.RecordCalls.Should().ContainSingle()
            .Which.Should().Be((CompanyId, DocId, ValidationPurpose.SelfBilledAcceptance, (Guid?)OperatorId, "Opérateur Test"));
    }

    [Fact]
    public async Task Contest_Without_Actions_Permission_Is_Refused()
    {
        var workflow = new RecordingWorkflow();
        var service = Build(workflow, canAct: false);

        var result = await service.ContestAsync(DocId, ValidationPurpose.SelfBilledAcceptance);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("liakont.actions");
        workflow.ContestCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Contest_Delegates_To_The_Workflow_On_Success()
    {
        var workflow = new RecordingWorkflow();
        var service = Build(workflow, canAct: true);

        var result = await service.ContestAsync(DocId, ValidationPurpose.CreditNoteAcceptance);

        result.Success.Should().BeTrue();
        workflow.ContestCalls.Should().ContainSingle()
            .Which.Should().Be((CompanyId, DocId, ValidationPurpose.CreditNoteAcceptance, (Guid?)OperatorId, "Opérateur Test"));
    }

    private static SignatureConsoleActionsService Build(IDocumentApprovalWorkflow workflow, bool canAct, bool tenantResolved = true)
    {
        var actor = new FakeActorContextAccessor(
            tenantResolved ? CompanyId : null, OperatorId, "Opérateur Test", authenticated: true);

        return new SignatureConsoleActionsService(
            workflow,
            actor,
            new FakePermissionService(canAct));
    }

    private sealed class RecordingWorkflow : IDocumentApprovalWorkflow
    {
        public List<(Guid CompanyId, Guid DocumentId, ValidationPurpose Purpose, DateTimeOffset? DeadlineUtc, Guid? OperatorId, string? OperatorName)> RequestCalls { get; } = [];

        public List<(Guid CompanyId, Guid DocumentId, ValidationPurpose Purpose, Guid? OperatorId, string? OperatorName)> RecordCalls { get; } = [];

        public List<(Guid CompanyId, Guid DocumentId, ValidationPurpose Purpose, Guid? OperatorId, string? OperatorName)> ContestCalls { get; } = [];

        public Exception? RequestThrows { get; set; }

        public Exception? TransitionThrows { get; set; }

        public Task RequestValidationAsync(Guid companyId, Guid documentId, ValidationPurpose purpose, DateTimeOffset? deadlineUtc, Guid? operatorId, string? operatorName, CancellationToken ct = default)
        {
            if (RequestThrows is not null)
            {
                throw RequestThrows;
            }

            RequestCalls.Add((companyId, documentId, purpose, deadlineUtc, operatorId, operatorName));
            return Task.CompletedTask;
        }

        public Task RecordRecordedValidationAsync(Guid companyId, Guid documentId, ValidationPurpose purpose, Guid? operatorId, string? operatorName, CancellationToken ct = default)
        {
            if (TransitionThrows is not null)
            {
                throw TransitionThrows;
            }

            RecordCalls.Add((companyId, documentId, purpose, operatorId, operatorName));
            return Task.CompletedTask;
        }

        public Task ContestAsync(Guid companyId, Guid documentId, ValidationPurpose purpose, Guid? operatorId, string? operatorName, CancellationToken ct = default)
        {
            if (TransitionThrows is not null)
            {
                throw TransitionThrows;
            }

            ContestCalls.Add((companyId, documentId, purpose, operatorId, operatorName));
            return Task.CompletedTask;
        }

        public Task<bool> RecordTacitValidationIfDueAsync(Guid companyId, Guid documentId, ValidationPurpose purpose, DateTimeOffset nowUtc, string? operatorName, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public FakeActorContextAccessor(Guid? companyId, Guid userId, string displayName, bool authenticated) =>
            Current = new FakeActorContext(companyId, userId, displayName, authenticated);

        public IActorContext Current { get; }

        private sealed class FakeActorContext : IActorContext
        {
            public FakeActorContext(Guid? companyId, Guid userId, string displayName, bool authenticated)
            {
                CompanyId = companyId;
                UserId = userId;
                DisplayName = displayName;
                IsAuthenticated = authenticated;
            }

            public Guid UserId { get; }

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated { get; }

            public string? DisplayName { get; }

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _canAct;

        public FakePermissionService(bool canAct) => _canAct = canAct;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _canAct && string.Equals(permission, "liakont.actions", StringComparison.Ordinal);
    }
}
