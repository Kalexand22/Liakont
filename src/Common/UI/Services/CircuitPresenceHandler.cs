namespace Stratum.Common.UI.Services;

using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Scoped <see cref="CircuitHandler"/> that cleans up all collaboration presence entries
/// when the Blazor circuit disconnects (browser closed, network failure, etc.).
/// </summary>
internal sealed partial class CircuitPresenceHandler : CircuitHandler
{
    private readonly ICollaborationService _collaborationService;
    private readonly CircuitPresenceRegistry _registry;
    private readonly ILogger<CircuitPresenceHandler> _logger;

    public CircuitPresenceHandler(
        ICollaborationService collaborationService,
        CircuitPresenceRegistry registry,
        ILogger<CircuitPresenceHandler> logger)
    {
        _collaborationService = collaborationService;
        _registry = registry;
        _logger = logger;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var circuitIds = _registry.GetAll();

        foreach (var virtualCircuitId in circuitIds)
        {
            _collaborationService.ClearFieldFocus(virtualCircuitId);
            _collaborationService.Untrack(virtualCircuitId);
            LogCircuitCleanup(virtualCircuitId);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleaned up presence for virtual circuit {VirtualCircuitId} on disconnect")]
    private partial void LogCircuitCleanup(string virtualCircuitId);
}
