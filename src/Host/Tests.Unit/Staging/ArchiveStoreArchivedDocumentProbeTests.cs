namespace Liakont.Host.Tests.Unit.Staging;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Staging;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Staging.Contracts;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Adaptateur de présence WORM du composition root (PIP00, ADR-0014). Vérifie que la dérivation du chemin
/// du manifest correspond EXACTEMENT à celle d'<c>ArchiveService</c> (sinon purge jamais déclenchée, ou
/// pire, mauvaise cible), que le sondage est tenant-scopé, et qu'un tenant non résolu échoue.
/// </summary>
public sealed class ArchiveStoreArchivedDocumentProbeTests
{
    [Fact]
    public async Task IsArchived_True_When_Manifest_Present_And_Queries_The_Canonical_Manifest_Path()
    {
        var locator = new ArchivedDocumentLocator(Guid.NewGuid(), 2026, 6, "INV-001");
        string expectedPath = ArchivePackageLayout.Combine(
            ArchivePackageLayout.PackageDirectory(2026, 6, "INV-001"),
            ArchivePackageLayout.ManifestFileName);
        var store = new RecordingArchiveStore(expectedPath);
        var probe = new ArchiveStoreArchivedDocumentProbe(store, new FakeTenantContext("tenant-a"));

        bool archived = await probe.IsArchivedAsync(locator);

        archived.Should().BeTrue();
        store.LastExistsQuery.Should().NotBeNull();
        store.LastExistsQuery!.Value.Tenant.Should().Be("tenant-a", "le sondage est tenant-scopé");
        store.LastExistsQuery!.Value.Path.Should().Be(expectedPath, "la sonde interroge le chemin du manifest scellé par ArchiveService");
    }

    [Fact]
    public async Task IsArchived_False_When_Manifest_Absent()
    {
        var store = new RecordingArchiveStore();
        var probe = new ArchiveStoreArchivedDocumentProbe(store, new FakeTenantContext("tenant-a"));

        bool archived = await probe.IsArchivedAsync(new ArchivedDocumentLocator(Guid.NewGuid(), 2026, 6, "INV-001"));

        archived.Should().BeFalse("WORM absent → purge suspendue (ADR-0014 §4)");
    }

    [Fact]
    public async Task Throws_When_Tenant_Unresolved()
    {
        var probe = new ArchiveStoreArchivedDocumentProbe(new RecordingArchiveStore(), new FakeTenantContext(tenantId: null));

        Func<Task> act = () => probe.IsArchivedAsync(new ArchivedDocumentLocator(Guid.NewGuid(), 2026, 6, "INV-001"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class RecordingArchiveStore : IArchiveStore
    {
        private readonly HashSet<string> _present;

        public RecordingArchiveStore(params string[] presentPaths)
        {
            _present = new HashSet<string>(presentPaths, StringComparer.Ordinal);
        }

        public (string Tenant, string Path)? LastExistsQuery { get; private set; }

        public ArchiveStoreCapabilities Capabilities => ArchiveStoreCapabilities.None;

        public Task WriteAsync(string tenant, string relativePath, byte[] content, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> ExistsAsync(string tenant, string relativePath, CancellationToken cancellationToken = default)
        {
            LastExistsQuery = (tenant, relativePath);
            return Task.FromResult(_present.Contains(relativePath));
        }

        public Task<byte[]> ReadAsync(string tenant, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<byte>());
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId)
        {
            TenantId = tenantId;
        }

        public string? TenantId { get; }

        public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
    }
}
