namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Identifiants STABLES et PARTAGÉS de l'action opérateur « Lancer un traitement maintenant » (déclenchement
/// manuel du traitement du tenant — runs/trigger, items API01b / WEB05). SOURCE UNIQUE consommée à la fois par
/// l'endpoint HTTP (<c>PipelineEndpointMapping</c>) et par le service in-process de la console
/// (<c>DocumentSendActionsService</c>, Host) — ces deux canaux exécutent le MÊME geste opérateur (le cookie OIDC
/// n'est pas disponible dans le circuit Blazor, d'où l'appel in-process), donc leur piste d'audit DOIT être
/// identique : centraliser ces identifiants ici empêche toute divergence silencieuse (CLAUDE.md n°4 — fidélité
/// de la piste d'audit). Le déclenchement lui-même reste une publication du déclencheur mono-tenant
/// <c>SendTenantTrigger</c> sur la queue SYSTÈME (ADR-0016) ; aucune règle métier ici.
/// </summary>
public static class PipelineRunActionContract
{
    /// <summary>Code d'audit du déclenchement manuel du traitement du tenant.</summary>
    public const string RunTriggeredActivity = "pipeline.run_triggered";

    /// <summary>Type d'entité de la piste d'audit du déclenchement manuel (non rattaché à un document unique).</summary>
    public const string RunEntityType = "PipelineRun";

    /// <summary>Identifiant d'entité de la piste d'audit du déclenchement manuel.</summary>
    public const string RunEntityId = "manual-trigger";
}
