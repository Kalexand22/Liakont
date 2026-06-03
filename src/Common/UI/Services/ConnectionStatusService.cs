namespace Stratum.Common.UI.Services;

using Microsoft.AspNetCore.Components.Server.Circuits;
using Stratum.Common.UI.Models;

/// <summary>
/// Scoped service that implements both <see cref="CircuitHandler"/> and
/// <see cref="IConnectionStatusService"/>. A single scoped instance is shared
/// between the Blazor circuit infrastructure and consumer components.
/// </summary>
internal sealed class ConnectionStatusService : CircuitHandler, IConnectionStatusService
{
    public event Action? OnStateChanged;

    public ConnectionState State { get; private set; } = ConnectionState.Connected;

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        State = ConnectionState.Reconnecting;
        OnStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        State = ConnectionState.Connected;
        OnStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Forces a specific state. Intended for demo and testing scenarios only.</summary>
    internal void SimulateState(ConnectionState state)
    {
        State = state;
        OnStateChanged?.Invoke();
    }
}
