namespace Liakont.Agent;

using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

/// <summary>
/// Installeur du service Windows, exécuté par <c>Liakont.Agent.exe install</c> /
/// <c>uninstall</c> via <see cref="ManagedInstallerClass"/> (voir <see cref="Program"/>).
/// <para>
/// Le service tourne sous <see cref="ServiceAccount.LocalSystem"/> : compte requis pour créer le
/// mutex <c>Global\</c> (<see cref="Liakont.Agent.Core.Hosting.InterProcessRunLock"/>) et pour
/// écrire sous <c>C:\ProgramData\Liakont\</c>. Démarrage automatique : la flotte d'agents doit
/// reprendre seule après un redémarrage du poste.
/// </para>
/// </summary>
[RunInstaller(true)]
public sealed class AgentServiceInstaller : Installer
{
    public AgentServiceInstaller()
    {
        var processInstaller = new ServiceProcessInstaller
        {
            Account = ServiceAccount.LocalSystem,
        };

        var serviceInstaller = new ServiceInstaller
        {
            ServiceName = "LiakontAgent",
            DisplayName = "Liakont Agent",
            Description = "Agent Liakont : extraction (lecture seule), tampon local et transmission HTTPS vers la plateforme.",
            StartType = ServiceStartMode.Automatic,
        };

        Installers.Add(processInstaller);
        Installers.Add(serviceInstaller);
    }
}
