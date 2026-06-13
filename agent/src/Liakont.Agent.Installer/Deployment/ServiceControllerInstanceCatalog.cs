namespace Liakont.Agent.Installer.Deployment;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ServiceProcess;
using Liakont.Agent.Core;
using Liakont.Agent.Installer.Configuration;

/// <summary>
/// Implémentation de production de <see cref="IInstalledInstanceCatalog"/> : énumère les services Windows
/// pour repérer les instances de l'agent déjà installées (multi-instances, OPS05 pt 5). Le mapping
/// service → instance s'appuie sur <see cref="AgentInstance"/> (« LiakontAgent » → Default ;
/// « LiakontAgent$&lt;nom&gt; » → instance nommée) — jamais redéfini ici. Un service au préfixe proche
/// mais au nom invalide est ignoré (il n'est pas une instance Liakont reconnue).
/// </summary>
internal sealed class ServiceControllerInstanceCatalog : IInstalledInstanceCatalog
{
    private const string NamedInstancePrefix = "LiakontAgent$";

    /// <inheritdoc />
    public IReadOnlyList<string> ListInstalledInstanceNames()
    {
        var names = new List<string>();
        ServiceController[] services;
        try
        {
            services = ServiceController.GetServices();
        }
        catch (Win32Exception ex)
        {
            // Échec d'accès au gestionnaire de services Windows (typiquement droits insuffisants) :
            // le port présente un contrat d'échec STABLE (InvalidOperationException) que l'appelant —
            // wizard ou « --list-instances » — sait dégrader en message opérateur français, plutôt
            // que de laisser une Win32Exception brute planter le démarrage de l'installeur.
            throw new InvalidOperationException(
                "Énumération des services Windows impossible (droits insuffisants ?). " +
                "Relancez l'installeur en tant qu'administrateur.",
                ex);
        }

        try
        {
            foreach (ServiceController service in services)
            {
                if (TryMapToInstanceName(service.ServiceName, out string instanceName))
                {
                    names.Add(instanceName);
                }
            }
        }
        finally
        {
            foreach (ServiceController service in services)
            {
                service.Dispose();
            }
        }

        return names;
    }

    private static bool TryMapToInstanceName(string serviceName, out string instanceName)
    {
        instanceName = string.Empty;

        if (string.Equals(serviceName, AgentInstance.Default.ServiceName, StringComparison.OrdinalIgnoreCase))
        {
            instanceName = AgentInstance.Default.Name;
            return true;
        }

        if (serviceName.StartsWith(NamedInstancePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string raw = serviceName.Substring(NamedInstancePrefix.Length);
            if (AgentInstance.TryParse(raw, out AgentInstance instance, out _))
            {
                instanceName = instance.Name;
                return true;
            }
        }

        return false;
    }
}
