namespace Stratum.Common.UI.Models;

/// <summary>Represents the current SignalR circuit connection state.</summary>
public enum ConnectionState
{
    /// <summary>Circuit is connected and functioning normally.</summary>
    Connected,

    /// <summary>Circuit lost — Blazor is attempting to reconnect.</summary>
    Reconnecting,

    /// <summary>
    /// Reconnection failed — the page must be refreshed.
    /// <para>
    /// <b>Blazor Server limitation:</b> <c>CircuitHandler</c> provides no terminal-disconnect
    /// callback. This state is only reachable via demo/test simulation
    /// (<c>ConnectionStatusService.SimulateState</c>) or a future JS-interop implementation.
    /// </para>
    /// </summary>
    Disconnected,
}
