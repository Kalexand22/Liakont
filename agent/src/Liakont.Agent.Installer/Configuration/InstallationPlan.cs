namespace Liakont.Agent.Installer.Configuration;

using System;

/// <summary>
/// Plan d'installation d'UNE instance de l'agent, produit par le moteur et exécuté par
/// <see cref="IAgentDeployer"/>. Il porte le nom d'instance VALIDÉ et le contenu complet de
/// <c>agent.json</c> — dont les secrets (clé API, chaîne ODBC) sont DÉJÀ chiffrés DPAPI (F13 §6,
/// CLAUDE.md n°10). Le déployeur n'a donc jamais à manipuler de secret en clair.
/// </summary>
internal sealed class InstallationPlan
{
    public InstallationPlan(string instanceName, string agentJson)
    {
        InstanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
        AgentJson = agentJson ?? throw new ArgumentNullException(nameof(agentJson));
    }

    /// <summary>Nom d'instance validé (« Default » ou un nom de service Windows valide).</summary>
    public string InstanceName { get; }

    /// <summary>Contenu de <c>agent.json</c> à écrire (secrets déjà chiffrés DPAPI).</summary>
    public string AgentJson { get; }
}
