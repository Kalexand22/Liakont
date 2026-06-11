namespace Liakont.Host.Tests.Unit.Reconciliation;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Reconciliation;
using Liakont.Host.Security;
using Liakont.Modules.Reconciliation.Contracts;
using Liakont.Modules.Reconciliation.Contracts.DTOs;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests unitaires de <see cref="ReconciliationConsoleService"/> (WEB08) : composition des trois files en
/// lecture, garde de permission <c>liakont.actions</c> (défense en profondeur), délégation au module avec
/// l'identité d'opérateur (nom affiché &gt; e-mail &gt; identifiant, comme l'endpoint API04), et mappage des
/// exceptions du domaine (introuvable / conflit) en messages opérateur — jamais d'exception remontée.
/// </summary>
public sealed class ReconciliationConsoleServiceTests
{
    private static readonly Guid EntryId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid DocumentId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task GetQueueAsync_Composes_The_Three_Lists()
    {
        var queries = new FakeQueries
        {
            Proposals = [Proposal()],
            Orphans = [Orphan()],
            DocumentsWithoutPdf = [WithoutPdf()],
        };
        var service = Build(queries: queries);

        var model = await service.GetQueueAsync();

        model.Proposals.Should().ContainSingle();
        model.Orphans.Should().ContainSingle();
        model.DocumentsWithoutPdf.Should().ContainSingle();
        model.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmProposalAsync_Without_Actions_Permission_Is_Refused()
    {
        var module = new RecordingService();
        var service = Build(module: module, permissions: []);

        var result = await service.ConfirmProposalAsync(EntryId);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("liakont.actions");
        module.ConfirmedEntryId.Should().BeNull("aucune action n'est déléguée au module sans permission");
    }

    [Fact]
    public async Task ConfirmProposalAsync_With_Permission_Delegates_With_Operator_Display_Name()
    {
        var module = new RecordingService();
        var service = Build(
            module: module,
            actor: new FakeActor(displayName: "Alice Martin", email: "alice@test.local", userId: Guid.NewGuid()));

        var result = await service.ConfirmProposalAsync(EntryId);

        result.Succeeded.Should().BeTrue();
        module.ConfirmedEntryId.Should().Be(EntryId);
        module.OperatorIdentity.Should().Be("Alice Martin", "le nom affiché prime pour la piste d'audit (comme l'endpoint API04)");
    }

    [Fact]
    public async Task Operator_Identity_Falls_Back_To_Email_Then_UserId()
    {
        var moduleEmail = new RecordingService();
        await Build(module: moduleEmail, actor: new FakeActor(displayName: null, email: "bob@test.local", userId: Guid.NewGuid()))
            .ConfirmProposalAsync(EntryId);
        moduleEmail.OperatorIdentity.Should().Be("bob@test.local");

        var userId = Guid.NewGuid();
        var moduleId = new RecordingService();
        await Build(module: moduleId, actor: new FakeActor(displayName: null, email: null, userId: userId))
            .ConfirmProposalAsync(EntryId);
        moduleId.OperatorIdentity.Should().Be(userId.ToString());
    }

    [Fact]
    public async Task ConfirmProposalAsync_Maps_NotFound_To_Operator_Failure()
    {
        var module = new RecordingService { ThrowOnConfirm = new NotFoundException("gone") };
        var service = Build(module: module);

        var result = await service.ConfirmProposalAsync(EntryId);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("introuvable");
    }

    [Fact]
    public async Task ConfirmProposalAsync_Maps_Conflict_To_Operator_Failure()
    {
        var module = new RecordingService { ThrowOnConfirm = new ConflictException("not pending") };
        var service = Build(module: module);

        var result = await service.ConfirmProposalAsync(EntryId);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("plus une proposition en attente");
    }

    [Fact]
    public async Task RejectProposalAsync_With_Permission_Delegates()
    {
        var module = new RecordingService();
        var service = Build(module: module);

        var result = await service.RejectProposalAsync(EntryId);

        result.Succeeded.Should().BeTrue();
        module.RejectedEntryId.Should().Be(EntryId);
    }

    [Fact]
    public async Task RejectProposalAsync_Without_Permission_Is_Refused()
    {
        var module = new RecordingService();
        var service = Build(module: module, permissions: []);

        var result = await service.RejectProposalAsync(EntryId);

        result.Succeeded.Should().BeFalse();
        module.RejectedEntryId.Should().BeNull();
    }

    [Fact]
    public async Task LinkManuallyAsync_With_Permission_Delegates_With_Document()
    {
        var module = new RecordingService();
        var service = Build(module: module);

        var result = await service.LinkManuallyAsync(EntryId, DocumentId);

        result.Succeeded.Should().BeTrue();
        module.LinkedEntryId.Should().Be(EntryId);
        module.LinkedDocumentId.Should().Be(DocumentId);
    }

    [Fact]
    public async Task LinkManuallyAsync_Without_Permission_Is_Refused()
    {
        var module = new RecordingService();
        var service = Build(module: module, permissions: []);

        var result = await service.LinkManuallyAsync(EntryId, DocumentId);

        result.Succeeded.Should().BeFalse();
        module.LinkedEntryId.Should().BeNull();
    }

    [Fact]
    public async Task GetQueueAsync_Without_Actions_Permission_Returns_Empty_Without_Querying()
    {
        var queries = new FakeQueries { Proposals = [Proposal()], Orphans = [Orphan()], DocumentsWithoutPdf = [WithoutPdf()] };
        var service = Build(queries: queries, permissions: []);

        var model = await service.GetQueueAsync();

        model.Proposals.Should().BeEmpty();
        model.Orphans.Should().BeEmpty();
        model.DocumentsWithoutPdf.Should().BeEmpty();
        queries.ReadCallCount.Should().Be(0, "sans permission, le module n'est pas interrogé");
    }

    private static ReconciliationConsoleService Build(
        FakeQueries? queries = null,
        RecordingService? module = null,
        FakeActor? actor = null,
        string[]? permissions = null) =>
        new(
            queries ?? new FakeQueries(),
            module ?? new RecordingService(),
            new FakeActorAccessor(actor ?? new FakeActor("Op", "op@test.local", Guid.NewGuid())),
            new FakePermissionService(permissions ?? [LiakontPermissions.Actions]));

    private static ReconciliationProposalDto Proposal() => new(
        Guid.NewGuid(), "pool-1", "f1.pdf", DocumentId, "TextMatching", "Medium", "date + montant", DateTimeOffset.UtcNow);

    private static OrphanPdfDto Orphan() => new(
        Guid.NewGuid(), "pool-2", "f2.pdf", "aucune correspondance", DateTimeOffset.UtcNow);

    private static DocumentWithoutPdfDto WithoutPdf() => new(
        Guid.NewGuid(), "FA-001", new DateOnly(2026, 5, 1), 1200m);

    private sealed class FakeQueries : IReconciliationQueries
    {
        public IReadOnlyList<ReconciliationProposalDto> Proposals { get; init; } = [];

        public IReadOnlyList<OrphanPdfDto> Orphans { get; init; } = [];

        public IReadOnlyList<DocumentWithoutPdfDto> DocumentsWithoutPdf { get; init; } = [];

        public int ReadCallCount { get; private set; }

        public Task<IReadOnlyList<ReconciliationProposalDto>> GetPendingProposalsAsync(CancellationToken cancellationToken = default)
        {
            ReadCallCount++;
            return Task.FromResult(Proposals);
        }

        public Task<IReadOnlyList<OrphanPdfDto>> GetOrphanPdfsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Orphans);

        public Task<IReadOnlyList<DocumentWithoutPdfDto>> GetIssuedDocumentsWithoutPdfAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentsWithoutPdf);

        public Task<ReconciliationPdfContent?> OpenQueueEntryPdfAsync(Guid queueEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReconciliationPdfContent?>(null);
    }

    private sealed class RecordingService : IReconciliationService
    {
        public Guid? ConfirmedEntryId { get; private set; }

        public Guid? RejectedEntryId { get; private set; }

        public Guid? LinkedEntryId { get; private set; }

        public Guid? LinkedDocumentId { get; private set; }

        public string? OperatorIdentity { get; private set; }

        public Exception? ThrowOnConfirm { get; init; }

        public Task<ReconciliationRunResult> RunForCurrentTenantAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ConfirmManualReconciliationAsync(Guid queueEntryId, Guid documentId, string operatorIdentity, CancellationToken cancellationToken = default)
        {
            LinkedEntryId = queueEntryId;
            LinkedDocumentId = documentId;
            OperatorIdentity = operatorIdentity;
            return Task.CompletedTask;
        }

        public Task ConfirmProposalAsync(Guid queueEntryId, string operatorIdentity, CancellationToken cancellationToken = default)
        {
            if (ThrowOnConfirm is not null)
            {
                throw ThrowOnConfirm;
            }

            ConfirmedEntryId = queueEntryId;
            OperatorIdentity = operatorIdentity;
            return Task.CompletedTask;
        }

        public Task RejectProposalAsync(Guid queueEntryId, string operatorIdentity, CancellationToken cancellationToken = default)
        {
            RejectedEntryId = queueEntryId;
            OperatorIdentity = operatorIdentity;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly HashSet<string> _permissions;

        public FakePermissionService(string[] permissions) =>
            _permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => _permissions.Contains(permission);
    }

    private sealed class FakeActorAccessor : IActorContextAccessor
    {
        public FakeActorAccessor(IActorContext current) => Current = current;

        public IActorContext Current { get; }
    }

    private sealed class FakeActor : IActorContext
    {
        public FakeActor(string? displayName, string? email, Guid userId)
        {
            DisplayName = displayName;
            Email = email;
            UserId = userId;
        }

        public Guid UserId { get; }

        public Guid CorrelationId { get; } = Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName { get; }

        public string? Email { get; }

        public Guid? CompanyId => null;

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }
}
