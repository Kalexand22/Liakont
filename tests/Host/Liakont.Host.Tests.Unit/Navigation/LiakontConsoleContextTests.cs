namespace Liakont.Host.Tests.Unit.Navigation;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Navigation;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Reconciliation.Contracts;
using Liakont.Modules.Reconciliation.Contracts.DTOs;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

public sealed class LiakontConsoleContextTests
{
    [Fact]
    public async Task ReconciliationAvailable_Should_Be_True_When_Pool_Has_Pdfs()
    {
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store, new FakeReconciliationQueries(), NonAdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();

        context.ReconciliationAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ReconciliationAvailable_Should_Be_False_When_Pool_Is_Empty()
    {
        var store = new FakeIngestedPdfStore([]);
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store, new FakeReconciliationQueries(), NonAdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();

        context.ReconciliationAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task ReconciliationAvailable_Should_Be_False_And_Store_Untouched_When_Tenant_Unresolved()
    {
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var context = new LiakontConsoleContext(new FakeTenantContext(null), store, new FakeReconciliationQueries(), NonAdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();

        context.ReconciliationAvailable.Should().BeFalse();
        store.ListCallCount.Should().Be(0, "aucune lecture du pool sans tenant résolu");
    }

    [Fact]
    public async Task EnsureInitializedAsync_Should_Be_Idempotent()
    {
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store, new FakeReconciliationQueries(), NonAdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();
        await context.EnsureInitializedAsync();

        store.ListCallCount.Should().Be(1, "le pool n'est lu qu'une seule fois par circuit");
    }

    [Fact]
    public async Task ReconciliationPendingCount_Should_Sum_Proposals_And_Orphans_When_Pool_Present()
    {
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var queries = new FakeReconciliationQueries
        {
            Proposals = [Proposal(), Proposal()],
            Orphans = [Orphan()],
        };
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store, queries, NonAdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();

        // 2 propositions + 1 orphelin = 3 ; les documents sans PDF ne comptent pas (pas d'action immédiate).
        context.ReconciliationPendingCount.Should().Be(3);
    }

    [Fact]
    public async Task ReconciliationPendingCount_Should_Be_Zero_And_Queue_Untouched_When_Pool_Empty()
    {
        var store = new FakeIngestedPdfStore([]);
        var queries = new FakeReconciliationQueries
        {
            Proposals = [Proposal()],
            Orphans = [Orphan()],
        };
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store, queries, NonAdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();

        context.ReconciliationPendingCount.Should().Be(0);
        queries.QueueReadCount.Should().Be(0, "pas de pool → on n'interroge pas la file de réconciliation");
    }

    [Fact]
    public async Task ReconciliationPendingCount_Should_Degrade_To_Zero_When_Queue_Read_Throws()
    {
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var queries = new ThrowingReconciliationQueries();
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store, queries, NonAdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();

        context.ReconciliationAvailable.Should().BeTrue();
        context.ReconciliationPendingCount.Should().Be(0);
    }

    [Fact]
    public async Task IsCrossTenantAdmin_Should_Be_True_For_A_Super_Admin_And_Skip_Tenant_Preload()
    {
        // RB1 : un super-admin (stratum-admin) opère en cross-tenant — IsCrossTenantAdmin est vrai et AUCUNE
        // surface tenant n'est pré-chargée (le pool n'est jamais lu), même si un tenant est résolu par défaut.
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var context = new LiakontConsoleContext(
            new FakeTenantContext("tenant-a"), store, new FakeReconciliationQueries(), AdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();

        context.IsCrossTenantAdmin.Should().BeTrue();
        context.ReconciliationAvailable.Should().BeFalse();
        store.ListCallCount.Should().Be(0, "un super-admin cross-tenant ne pré-charge aucune surface tenant");
    }

    [Fact]
    public async Task IsCrossTenantAdmin_Should_Be_False_For_A_Non_Admin_User()
    {
        var store = new FakeIngestedPdfStore([]);
        var context = new LiakontConsoleContext(
            new FakeTenantContext("tenant-a"), store, new FakeReconciliationQueries(), NonAdminAuth(), NullLogger<LiakontConsoleContext>.Instance);

        await context.EnsureInitializedAsync();

        context.IsCrossTenantAdmin.Should().BeFalse();
    }

    private static FakeAuthStateProvider NonAdminAuth() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private static FakeAuthStateProvider AdminAuth() =>
        new(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Role, "stratum-admin")], authenticationType: "test")));

    private static ReconciliationProposalDto Proposal() => new(
        Guid.NewGuid(), "pool", "f.pdf", Guid.NewGuid(), "TextMatching", "Medium", "détail", DateTimeOffset.UtcNow);

    private static OrphanPdfDto Orphan() => new(
        Guid.NewGuid(), "pool", "o.pdf", "orphelin", DateTimeOffset.UtcNow);

    private sealed class FakeReconciliationQueries : IReconciliationQueries
    {
        public IReadOnlyList<ReconciliationProposalDto> Proposals { get; init; } = [];

        public IReadOnlyList<OrphanPdfDto> Orphans { get; init; } = [];

        public int QueueReadCount { get; private set; }

        public Task<IReadOnlyList<ReconciliationProposalDto>> GetPendingProposalsAsync(CancellationToken cancellationToken = default)
        {
            QueueReadCount++;
            return Task.FromResult(Proposals);
        }

        public Task<IReadOnlyList<OrphanPdfDto>> GetOrphanPdfsAsync(CancellationToken cancellationToken = default)
        {
            QueueReadCount++;
            return Task.FromResult(Orphans);
        }

        public Task<IReadOnlyList<DocumentWithoutPdfDto>> GetIssuedDocumentsWithoutPdfAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DocumentWithoutPdfDto>>([]);

        public Task<ReconciliationPdfContent?> OpenQueueEntryPdfAsync(Guid queueEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReconciliationPdfContent?>(null);
    }

    private sealed class FakeIngestedPdfStore : IIngestedPdfStore
    {
        private readonly IReadOnlyList<PooledPdfReference> _pooled;

        public FakeIngestedPdfStore(IReadOnlyList<PooledPdfReference> pooled) => _pooled = pooled;

        public int ListCallCount { get; private set; }

        public Task<IReadOnlyList<PooledPdfReference>> ListPooledPdfsAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            return Task.FromResult(_pooled);
        }

        public Task<string> SaveLinkedPdfAsync(string tenantId, string sourceReference, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> SavePooledPdfAsync(string tenantId, string fileName, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream> OpenPooledPdfAsync(string tenantId, string poolPdfId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> LinkedPdfExistsAsync(string tenantId, string sourceReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream?> TryOpenLinkedPdfAsync(string tenantId, string sourceReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }

    private sealed class FakeAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _user;

        public FakeAuthStateProvider(ClaimsPrincipal user) => _user = user;

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(_user));
    }

    private sealed class ThrowingReconciliationQueries : IReconciliationQueries
    {
        public Task<IReadOnlyList<ReconciliationProposalDto>> GetPendingProposalsAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");

        public Task<IReadOnlyList<OrphanPdfDto>> GetOrphanPdfsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrphanPdfDto>>([]);

        public Task<IReadOnlyList<DocumentWithoutPdfDto>> GetIssuedDocumentsWithoutPdfAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DocumentWithoutPdfDto>>([]);

        public Task<ReconciliationPdfContent?> OpenQueueEntryPdfAsync(Guid queueEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReconciliationPdfContent?>(null);
    }
}
