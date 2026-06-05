namespace Liakont.Modules.Staging.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Staging.Infrastructure;
using Xunit;

public sealed class StagingPurgeServiceTests
{
    [Fact]
    public async Task Purge_Quand_Le_Paquet_WORM_Est_Present() // INV-STAGING-007
    {
        var store = new RecordingStagingStore();
        var service = new StagingPurgeService(store, new FixedArchivedProbe(isArchived: true));
        var key = new StagedPayloadKey("tenant-a", Guid.NewGuid(), "hash");
        var locator = new ArchivedDocumentLocator(key.DocumentId, 2026, 6, "INV-001");

        bool purged = await service.PurgeIfArchivedAsync(key, locator);

        purged.Should().BeTrue();
        store.PurgedKeys.Should().ContainSingle().Which.Should().Be(key);
    }

    [Fact]
    public async Task Conserve_Quand_Le_Paquet_WORM_Est_Absent() // INV-STAGING-007
    {
        var store = new RecordingStagingStore();
        var service = new StagingPurgeService(store, new FixedArchivedProbe(isArchived: false));
        var key = new StagedPayloadKey("tenant-a", Guid.NewGuid(), "hash");
        var locator = new ArchivedDocumentLocator(key.DocumentId, 2026, 6, "INV-001");

        bool purged = await service.PurgeIfArchivedAsync(key, locator);

        purged.Should().BeFalse();
        store.PurgedKeys.Should().BeEmpty("entre la transition Issued et l'écriture WORM, le staging est CONSERVÉ (ADR-0014 §4)");
    }

    private sealed class FixedArchivedProbe : IArchivedDocumentProbe
    {
        private readonly bool _isArchived;

        public FixedArchivedProbe(bool isArchived)
        {
            _isArchived = isArchived;
        }

        public Task<bool> IsArchivedAsync(ArchivedDocumentLocator locator, CancellationToken cancellationToken = default) =>
            Task.FromResult(_isArchived);
    }

    private sealed class RecordingStagingStore : IPayloadStagingStore
    {
        public List<StagedPayloadKey> PurgedKeys { get; } = new();

        public PayloadStagingStoreCapabilities Capabilities => PayloadStagingStoreCapabilities.None;

        public Task WriteAsync(StagedPayloadKey key, string canonicalJson, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> ReadAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<bool> ExistsAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task PurgeAsync(StagedPayloadKey key, CancellationToken cancellationToken = default)
        {
            PurgedKeys.Add(key);
            return Task.CompletedTask;
        }
    }
}
