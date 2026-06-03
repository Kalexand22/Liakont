namespace Liakont.Agent.Contracts;

/// <summary>
/// Version du contrat de transport agent vers plateforme (préfixe d'URL /api/agent/v{N}/).
/// La plateforme supporte la version courante et la précédente (blueprint.md §3.2) ;
/// les DTOs réels du pivot EN 16931 arrivent avec PIV01. Cet assembly reste sans
/// dépendance hors BCL : aucun type ici ne porte de logique.
/// </summary>
public static class AgentContractVersion
{
    /// <summary>Version courante du contrat émise par l'agent.</summary>
    public const string Current = "v1";
}
