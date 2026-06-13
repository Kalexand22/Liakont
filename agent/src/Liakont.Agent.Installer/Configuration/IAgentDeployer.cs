namespace Liakont.Agent.Installer.Configuration;

/// <summary>
/// Port de DÉPLOIEMENT d'une instance de l'agent (F13 §4, étape finale) : écriture de <c>agent.json</c>,
/// création/suppression du service Windows (self-install AGT01), puis <c>check-config</c> final. Chaque
/// instance est ciblée par son nom — l'installation d'une nouvelle instance ne touche jamais les autres
/// (multi-instances, OPS05 pt 5). L'implémentation de production effectue les E/S réelles ; les tests
/// injectent une doublure (« Core mocké »), d'où la testabilité du moteur hors machine réelle.
/// </summary>
internal interface IAgentDeployer
{
    /// <summary>Déploie l'instance décrite par <paramref name="plan"/> (config + service + check-config).</summary>
    DeploymentOutcome Install(InstallationPlan plan);

    /// <summary>Désinstalle l'instance <paramref name="instanceName"/> (et elle seule), sans toucher aux autres.</summary>
    DeploymentOutcome Uninstall(string instanceName);
}
