namespace Liakont.Host.Tests.Unit.Navigation;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Navigation;
using Liakont.Modules.Ingestion.Contracts;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

public sealed class LiakontConsoleContextTests
{
    [Fact]
    public async Task ReconciliationAvailable_Should_Be_True_When_Pool_Has_Pdfs()
    {
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store);

        await context.EnsureInitializedAsync();

        context.ReconciliationAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ReconciliationAvailable_Should_Be_False_When_Pool_Is_Empty()
    {
        var store = new FakeIngestedPdfStore([]);
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store);

        await context.EnsureInitializedAsync();

        context.ReconciliationAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task ReconciliationAvailable_Should_Be_False_And_Store_Untouched_When_Tenant_Unresolved()
    {
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var context = new LiakontConsoleContext(new FakeTenantContext(null), store);

        await context.EnsureInitializedAsync();

        context.ReconciliationAvailable.Should().BeFalse();
        store.ListCallCount.Should().Be(0, "aucune lecture du pool sans tenant résolu");
    }

    [Fact]
    public async Task EnsureInitializedAsync_Should_Be_Idempotent()
    {
        var store = new FakeIngestedPdfStore([new PooledPdfReference("p1", "facture.pdf")]);
        var context = new LiakontConsoleContext(new FakeTenantContext("tenant-a"), store);

        await context.EnsureInitializedAsync();
        await context.EnsureInitializedAsync();

        store.ListCallCount.Should().Be(1, "le pool n'est lu qu'une seule fois par circuit");
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
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }
}
