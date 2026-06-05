namespace Liakont.Agent.Core.Update;

using System;

/// <summary>
/// Statut de la DERNIÈRE tentative d'auto-update, persisté localement (fichier statut) et surfacé au
/// heartbeat suivant (F12 §2.5 « signalement »). Écrit par l'agent (refus / différé / lancement) ET
/// par l'updater détaché (résultat final : appliqué / rollback). Tampon technique local, jamais une
/// piste d'audit.
/// </summary>
public sealed class AutoUpdateStatus
{
    /// <summary>Crée un statut d'auto-update.</summary>
    /// <param name="targetVersion">Version visée par la tentative.</param>
    /// <param name="phase">Étape atteinte (ex. « refus-signature », « lancé », « appliqué », « rollback »).</param>
    /// <param name="succeeded">Vrai si la tentative s'est conclue sans échec ni refus.</param>
    /// <param name="message">Message opérateur en français.</param>
    /// <param name="atUtc">Horodatage UTC de la tentative.</param>
    public AutoUpdateStatus(string? targetVersion, string phase, bool succeeded, string message, DateTime atUtc)
    {
        TargetVersion = targetVersion;
        Phase = phase ?? string.Empty;
        Succeeded = succeeded;
        Message = message ?? string.Empty;
        AtUtc = atUtc;
    }

    /// <summary>Version visée par la tentative.</summary>
    public string? TargetVersion { get; }

    /// <summary>Étape atteinte.</summary>
    public string Phase { get; }

    /// <summary>Vrai si la tentative s'est conclue sans échec ni refus.</summary>
    public bool Succeeded { get; }

    /// <summary>Message opérateur en français.</summary>
    public string Message { get; }

    /// <summary>Horodatage UTC de la tentative.</summary>
    public DateTime AtUtc { get; }
}
