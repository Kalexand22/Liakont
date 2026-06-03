namespace Stratum.Common.Infrastructure.Collaboration;

using Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// No-op implementation of <see cref="ICircuitNotifier"/>.
/// Replaced by the real Blazor circuit notifier in CE04.
/// </summary>
internal sealed class NullCircuitNotifier : ICircuitNotifier
{
    public Task NotifyEntityChangedAsync(EntityChangedEvent evt, IReadOnlyList<PresenceEntry> circuits)
        => Task.CompletedTask;
}
