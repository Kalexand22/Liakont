namespace Liakont.Agent.Installer.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Issue de l'exécution d'un <see cref="InstallationPlan"/> (ou d'une désinstallation) par
/// <see cref="IAgentDeployer"/> : succès global + rapport point par point en français (« check-config
/// final affiché en clair », F13 §4). Aucun secret n'apparaît jamais dans le rapport (CLAUDE.md n°10).
/// </summary>
internal sealed class DeploymentOutcome
{
    public DeploymentOutcome(bool success, IReadOnlyList<string> reportLines)
    {
        Success = success;
        ReportLines = reportLines ?? throw new ArgumentNullException(nameof(reportLines));
    }

    /// <summary>Vrai si tous les points de l'installation/désinstallation sont passés.</summary>
    public bool Success { get; }

    /// <summary>Rapport opérateur ligne par ligne (français), à afficher tel quel.</summary>
    public IReadOnlyList<string> ReportLines { get; }

    /// <summary>Construit une issue d'échec à partir d'un message unique.</summary>
    public static DeploymentOutcome Failure(string message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return new DeploymentOutcome(false, new[] { message }.ToList());
    }
}
