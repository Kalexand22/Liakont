namespace Liakont.Agent.Updater;

using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

/// <summary>
/// Sonde réelle de santé du redémarrage (ADR-0013) : le service doit être « en cours » ET le marqueur
/// de heartbeat local doit avoir été RAFRAÎCHI après le début de l'attente (preuve qu'un cycle de
/// heartbeat sain a eu lieu avec la NOUVELLE version). Sans confirmation dans le délai, l'updater
/// déclenche le rollback.
/// </summary>
public sealed class LocalHeartbeatHealthProbe : IServiceHealthProbe
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    /// <inheritdoc/>
    public bool WaitUntilHealthy(string serviceName, string heartbeatMarkerPath, TimeSpan timeout)
    {
        DateTime startUtc = DateTime.UtcNow;
        DateTime deadline = startUtc + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (IsServiceRunning(serviceName) && IsHeartbeatFresh(heartbeatMarkerPath, startUtc))
            {
                return true;
            }

            Thread.Sleep(PollInterval);
        }

        return IsServiceRunning(serviceName) && IsHeartbeatFresh(heartbeatMarkerPath, startUtc);
    }

    private static bool IsServiceRunning(string serviceName)
    {
        try
        {
            using (var controller = new ServiceController(serviceName))
            {
                controller.Refresh();
                return controller.Status == ServiceControllerStatus.Running;
            }
        }
        catch (InvalidOperationException)
        {
            // Service introuvable / inaccessible : non sain.
            return false;
        }
    }

    private static bool IsHeartbeatFresh(string heartbeatMarkerPath, DateTime sinceUtc)
    {
        try
        {
            return File.Exists(heartbeatMarkerPath) && File.GetLastWriteTimeUtc(heartbeatMarkerPath) >= sinceUtc;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
