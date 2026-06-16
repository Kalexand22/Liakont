namespace Liakont.Modules.SupportTrace.Tests.Unit;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.SupportTrace.Contracts;
using Liakont.Modules.SupportTrace.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Le service de purge applique la rétention configurée : la borne passée au store est
/// <c>maintenant − RetentionDays</c> (horloge injectée, déterministe), et une rétention non positive est
/// refusée (jamais de purge totale par mauvais paramétrage). Le tenant est propagé tel quel.
/// </summary>
public sealed class SupportTracePurgeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PurgeExpired_Passes_Cutoff_At_Now_Minus_Retention()
    {
        var store = new RecordingStore();
        var service = new SupportTracePurgeService(
            store,
            Options.Create(new SupportTraceOptions { RootPath = "x", RetentionDays = 90 }),
            new FixedTimeProvider(Now));

        await service.PurgeExpiredAsync("tenant-a");

        store.LastTenantId.Should().Be("tenant-a");
        store.LastCutoff.Should().Be(Now - TimeSpan.FromDays(90));
    }

    [Fact]
    public async Task PurgeExpired_Rejects_A_Non_Positive_Retention()
    {
        var service = new SupportTracePurgeService(
            new RecordingStore(),
            Options.Create(new SupportTraceOptions { RootPath = "x", RetentionDays = 0 }),
            new FixedTimeProvider(Now));

        var act = async () => await service.PurgeExpiredAsync("tenant-a");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class RecordingStore : ISupportTraceStore
    {
        public string? LastTenantId { get; private set; }

        public DateTimeOffset LastCutoff { get; private set; }

        public Task WriteAsync(string tenantId, Guid documentId, ReadOnlyMemory<byte> facturX, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<byte[]?> ReadAsync(string tenantId, Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public Task<int> PurgeOlderThanAsync(string tenantId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
        {
            LastTenantId = tenantId;
            LastCutoff = cutoffUtc;
            return Task.FromResult(0);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
