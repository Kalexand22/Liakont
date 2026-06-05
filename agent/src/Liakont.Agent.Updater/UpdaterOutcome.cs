namespace Liakont.Agent.Updater;

/// <summary>Issue d'un cycle d'updater (ADR-0013).</summary>
public enum UpdaterOutcome
{
    /// <summary>Nouvelle version installée et redémarrée sainement.</summary>
    Applied = 0,

    /// <summary>Nouvelle version non saine : anciens binaires restaurés et redémarrés.</summary>
    RolledBack = 1,

    /// <summary>Échec non récupéré (y compris l'échec du rollback lui-même).</summary>
    Failed = 2,
}
