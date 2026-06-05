namespace Liakont.Agent.Updater;

using System;
using System.ServiceProcess;

/// <summary>Pilotage réel du service via le Gestionnaire de contrôle de services (SCM) Windows.</summary>
public sealed class ScmServiceControl : IServiceControl
{
    /// <inheritdoc/>
    public void StopService(string serviceName, TimeSpan timeout)
    {
        using (var controller = new ServiceController(serviceName))
        {
            controller.Refresh();
            if (controller.Status != ServiceControllerStatus.Stopped && controller.Status != ServiceControllerStatus.StopPending)
            {
                controller.Stop();
            }

            controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        }
    }

    /// <inheritdoc/>
    public void StartService(string serviceName, TimeSpan timeout)
    {
        using (var controller = new ServiceController(serviceName))
        {
            controller.Refresh();
            if (controller.Status != ServiceControllerStatus.Running && controller.Status != ServiceControllerStatus.StartPending)
            {
                controller.Start();
            }

            controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
        }
    }
}
