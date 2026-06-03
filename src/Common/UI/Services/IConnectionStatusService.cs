namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Tracks the current Blazor Server circuit connection state.
/// Register via <c>AddCommonUI()</c>. Place a <c>&lt;ConnectionStatus /&gt;</c>
/// in the layout to surface reconnection banners.
/// </summary>
public interface IConnectionStatusService
{
    /// <summary>Raised on the circuit's synchronisation context when <see cref="State"/> changes.</summary>
    event Action? OnStateChanged;

    /// <summary>Current connection state. Defaults to <see cref="ConnectionState.Connected"/>.</summary>
    ConnectionState State { get; }
}
