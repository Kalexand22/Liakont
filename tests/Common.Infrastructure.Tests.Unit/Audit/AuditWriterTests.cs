namespace Stratum.Common.Infrastructure.Tests.Unit.Audit;

using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Infrastructure.Audit;
using Stratum.Common.Infrastructure.Database;
using Xunit;

public sealed class AuditWriterTests
{
    [Fact]
    public async Task WriteChangeAsync_Should_NotThrow_When_ConnectionFactoryThrows()
    {
        var writer = new AuditWriter(new FailingConnectionFactory(), NullLogger<AuditWriter>.Instance);

        var act = async () => await writer.WriteChangeAsync(
            Guid.NewGuid(), "Party", "abc", "name", "old", "new", "user1");

        await act.Should().NotThrowAsync(
            because: "INV-AUDIT-002: audit writes must never fail the business transaction");
    }

    [Fact]
    public async Task WriteChangeAsync_Should_NotThrow_When_ConnectionFailsWithNullValues()
    {
        var writer = new AuditWriter(new FailingConnectionFactory(), NullLogger<AuditWriter>.Instance);

        var act = async () => await writer.WriteChangeAsync(
            Guid.NewGuid(), "Party", "abc", "name", oldValue: null, newValue: null, actorId: "system");

        await act.Should().NotThrowAsync(
            because: "INV-AUDIT-002 applies regardless of whether old/new values are null");
    }

    [Fact]
    public async Task WriteChangeAsync_Should_Throw_When_TokenAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var writer = new AuditWriter(new CancellationRespectingConnectionFactory(), NullLogger<AuditWriter>.Instance);

        var act = async () => await writer.WriteChangeAsync(
            Guid.NewGuid(), "Party", "abc", "name", "old", "new", "user1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "cancellation must not be swallowed — callers depend on cooperative shutdown");
    }

    // ── Fakes ──────────────────────────────────────────────────────────────
    private sealed class FailingConnectionFactory : ISystemConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
            => Task.FromException<IDbConnection>(new InvalidOperationException("DB unavailable"));
    }

    private sealed class CancellationRespectingConnectionFactory : ISystemConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<IDbConnection>(new InvalidOperationException("unreachable"));
        }
    }
}
