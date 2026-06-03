namespace Stratum.Common.Infrastructure.Tests.Unit.Collaboration;

using FluentAssertions;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Infrastructure.Collaboration;
using Xunit;

public sealed class NullCircuitNotifierTests
{
    [Fact]
    public async Task NotifyEntityChangedAsync_Should_CompleteImmediately()
    {
        var sut = new NullCircuitNotifier();

        var evt = new EntityChangedEvent("Quote", "42", "alice", DateTimeOffset.UtcNow);
        var circuits = new List<PresenceEntry> { new("circuit-1", "alice") };

        var act = () => sut.NotifyEntityChangedAsync(evt, circuits);

        await act.Should().NotThrowAsync();
    }
}
