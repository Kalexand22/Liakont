namespace Liakont.Agent.Core.Update;

/// <summary>
/// Résultat typé d'un appel d'auto-update : l'issue mécanique, la version visée (si connue) et un
/// message opérateur en français (CLAUDE.md n°12). Aucune exception ne franchit la couche update —
/// un échec est une <see cref="AutoUpdateOutcome"/>, pas une levée (même contrat que le heartbeat).
/// </summary>
public sealed class AutoUpdateResult
{
    /// <summary>Crée un résultat d'auto-update.</summary>
    /// <param name="outcome">L'issue mécanique de la tentative.</param>
    /// <param name="message">Message opérateur (français).</param>
    /// <param name="targetVersion">Version visée par la mise à jour, si elle a pu être lue.</param>
    public AutoUpdateResult(AutoUpdateOutcome outcome, string message, string? targetVersion = null)
    {
        Outcome = outcome;
        Message = message ?? string.Empty;
        TargetVersion = targetVersion;
    }

    /// <summary>L'issue mécanique de la tentative.</summary>
    public AutoUpdateOutcome Outcome { get; }

    /// <summary>Message opérateur en français.</summary>
    public string Message { get; }

    /// <summary>Version visée par la mise à jour (si lisible).</summary>
    public string? TargetVersion { get; }

    /// <summary>Vrai si la tentative a abouti au lancement de l'updater.</summary>
    public bool Launched => Outcome == AutoUpdateOutcome.Launched;
}
