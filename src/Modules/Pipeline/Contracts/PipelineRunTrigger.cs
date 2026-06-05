namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Origine d'une exécution du pipeline : déclenchée manuellement (console / API) ou planifiée
/// (job tenant, mécanique <c>TenantJobRunner</c> — SOL06).
/// </summary>
public enum PipelineRunTrigger
{
    /// <summary>Déclenchement manuel (opérateur via la console / l'API).</summary>
    Manual = 0,

    /// <summary>Déclenchement planifié (job tenant, jamais une boucle multi-tenant locale — SOL06).</summary>
    Scheduled = 1,
}
