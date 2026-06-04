namespace Liakont.Agent.Contracts.Transport;

/// <summary>
/// Noms des en-têtes HTTP du contrat d'ingestion (F12 §3.1), partagés par l'agent (émission) et la
/// plateforme (vérification). Constantes pures : aucune logique, aucune dépendance hors BCL.
/// </summary>
public static class AgentApiHeaders
{
    /// <summary>En-tête portant la clé API de l'agent (<c>prefix.secret</c>).</summary>
    public const string AgentKey = "X-Agent-Key";

    /// <summary>En-tête portant la version du contrat déclarée par l'agent (négociation 426).</summary>
    public const string ContractVersion = "X-Contract-Version";
}
