namespace Liakont.Agent;

using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;
using Liakont.Agent.Core;

/// <summary>
/// Installeur du service Windows, exécuté par <c>Liakont.Agent.exe install</c> /
/// <c>uninstall</c> via <see cref="ManagedInstallerClass"/> (voir <see cref="Program"/>).
/// <para>
/// MULTI-INSTANCES (OPS05 pt 5) : le paramètre d'installation <c>/instance=&lt;nom&gt;</c>
/// (passé par <see cref="Program"/>) détermine le nom du service
/// (<see cref="AgentInstance.ServiceName"/>). Pour une instance nommée, le chemin d'image du
/// service est enrichi de <c>--instance &lt;nom&gt;</c> afin que le processus démarre sur la
/// bonne instance ; l'instance par défaut conserve le chemin d'image historique inchangé.
/// La désinstallation exige le MÊME paramètre <c>/instance=</c> pour cibler le bon service.
/// </para>
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
    private readonly ServiceInstaller _serviceInstaller;

    public AgentServiceInstaller()
    {
        var processInstaller = new ServiceProcessInstaller
        {
            Account = ServiceAccount.LocalSystem,
        };

        _serviceInstaller = new ServiceInstaller
        {
            ServiceName = AgentInstance.Default.ServiceName,
            DisplayName = AgentInstance.Default.DisplayName,
            Description = "Agent Liakont : extraction (lecture seule), tampon local et transmission HTTPS vers la plateforme.",
            StartType = ServiceStartMode.Automatic,
        };

        Installers.Add(processInstaller);
        Installers.Add(_serviceInstaller);
    }

    /// <inheritdoc />
    protected override void OnBeforeInstall(IDictionary savedState)
    {
        AgentInstance instance = ResolveInstance();
        if (!instance.IsDefault)
        {
            // Le service doit démarrer sur SON instance : on ajoute --instance au chemin d'image.
            // Le chemin est cité (espaces dans Program Files) ; ManagedInstallerClass le passe brut.
            string assemblyPath = Context.Parameters["assemblypath"];
            if (!string.IsNullOrEmpty(assemblyPath) && !assemblyPath.StartsWith("\"", System.StringComparison.Ordinal))
            {
                assemblyPath = "\"" + assemblyPath + "\"";
            }

            Context.Parameters["assemblypath"] =
                assemblyPath + " " + AgentInstance.CommandLineOption + " " + instance.Name;
        }

        base.OnBeforeInstall(savedState);
    }

    /// <inheritdoc />
    protected override void OnBeforeUninstall(IDictionary savedState)
    {
        ResolveInstance();
        base.OnBeforeUninstall(savedState);
    }

    /// <summary>
    /// Lit <c>/instance=</c> du contexte d'installation et applique le nom de service correspondant.
    /// Paramètre absent ou vide = instance par défaut (comportement historique).
    /// </summary>
    private AgentInstance ResolveInstance()
    {
        string? rawName = Context?.Parameters?["instance"];
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return AgentInstance.Default;
        }

        if (!AgentInstance.TryParse(rawName, out AgentInstance instance, out string? error))
        {
            throw new InstallException(error);
        }

        _serviceInstaller.ServiceName = instance.ServiceName;
        _serviceInstaller.DisplayName = instance.DisplayName;
        return instance;
    }
}
