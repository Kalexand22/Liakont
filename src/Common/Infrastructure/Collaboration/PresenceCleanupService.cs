namespace Stratum.Common.Infrastructure.Collaboration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Background service that periodically purges expired field focus entries
/// from the collaboration service. Runs every 60 seconds.
/// </summary>
internal sealed partial class PresenceCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly ICollaborationService _collaborationService;
    private readonly ILogger<PresenceCleanupService> _logger;

    public PresenceCleanupService(
        ICollaborationService collaborationService,
        ILogger<PresenceCleanupService> logger)
    {
        _collaborationService = collaborationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    _collaborationService.PurgeExpiredEntries();
                    LogPurgeCompleted();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogPurgeError(ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — PeriodicTimer throws when the stopping token is cancelled.
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Presence cleanup: purged expired field focus entries")]
    private partial void LogPurgeCompleted();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Presence cleanup: error during purge")]
    private partial void LogPurgeError(Exception exception);
}
