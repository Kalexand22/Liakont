namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Origine d'une exécution du pipeline : déclenchée manuellement (console / API), planifiée
/// (job tenant, mécanique <c>TenantJobRunner</c> — SOL06) ou en réaction à un événement d'intégration
/// (CHECK consomme <c>DocumentReceivedV1</c> via l'outbox — PIP01b).
/// </summary>
public enum PipelineRunTrigger
{
    /// <summary>Déclenchement manuel (opérateur via la console / l'API).</summary>
    Manual = 0,

    /// <summary>Déclenchement planifié (job tenant, jamais une boucle multi-tenant locale — SOL06).</summary>
    Scheduled = 1,

    /// <summary>Déclenchement par un événement d'intégration de l'outbox (CHECK sur <c>DocumentReceivedV1</c> — PIP01b).</summary>
    Event = 2,
}
