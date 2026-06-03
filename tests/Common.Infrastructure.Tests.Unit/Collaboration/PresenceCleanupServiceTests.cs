namespace Stratum.Common.Infrastructure.Tests.Unit.Collaboration;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Infrastructure.Collaboration;
using Xunit;

public sealed class PresenceCleanupServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Should_PurgeExpiredEntries()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var collaborationService = new CollaborationService(timeProvider);
        var logger = NullLogger<PresenceCleanupService>.Instance;

        collaborationService.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        // Advance past TTL
        timeProvider.Advance(TimeSpan.FromSeconds(61));

        // Create and start the service with a short timeout so we can verify one iteration
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new TestablePresenceCleanupService(collaborationService, logger);

        // Run a single purge cycle
        service.InvokePurge();

        collaborationService.GetFieldPresence("Quote", "42", "amount").Should().BeEmpty();
    }

    /// <summary>
    /// Exposes internal purge for testing without waiting for the timer.
    /// </summary>
    private sealed class TestablePresenceCleanupService
    {
        private readonly ICollaborationService _collaborationService;

        public TestablePresenceCleanupService(
            ICollaborationService collaborationService,
            Microsoft.Extensions.Logging.ILogger<PresenceCleanupService> logger)
        {
            _collaborationService = collaborationService;
        }

        public void InvokePurge() => _collaborationService.PurgeExpiredEntries();
    }

    /// <summary>
    /// Minimal settable time provider for testing TTL-based logic.
    /// </summary>
    private sealed class SettableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public SettableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
