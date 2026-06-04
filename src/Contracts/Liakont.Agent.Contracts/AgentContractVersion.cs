namespace Liakont.Agent.Contracts;

/// <summary>
/// Version du contrat de transport agent vers plateforme (préfixe d'URL /api/agent/v{N}/).
/// La plateforme supporte la version courante et la précédente (blueprint.md §3.2).
/// Les DTOs du pivot EN 16931 (PIV01) vivent dans <c>Liakont.Agent.Contracts.Pivot</c> et les
/// DTOs d'enveloppe du contrat dans <c>Liakont.Agent.Contracts.Transport</c>. Cet assembly reste
/// sans dépendance hors BCL : aucun type ici ne porte de logique métier.
/// </summary>
public static class AgentContractVersion
{
    /// <summary>Préfixe d'URL de la version courante du contrat (ex. <c>/api/agent/v1/</c>).</summary>
    public const string Current = "v1";

    /// <summary>
    /// Numéro de version du payload du contrat porté par cet assembly (F12 §6.4 — matrice de
    /// compatibilité). En V1, un champ s'AJOUTE et ne se renomme/supprime jamais ; toute rupture
    /// est une nouvelle version (PIV03 documente les règles d'évolution).
    /// </summary>
    public const string ContractVersion = "1";
}
