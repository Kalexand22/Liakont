namespace Liakont.Modules.Ingestion.Contracts;

using Liakont.Agent.Contracts;

/// <summary>
/// Politique de compatibilité du contrat agent↔plateforme (F12 §3.1, §6.4). La plateforme accepte
/// la version courante et la précédente (N et N-1) ; toute autre version (inconnue ou trop ancienne)
/// est refusée par un <c>426 Upgrade Required</c> qui déclenche l'auto-update de l'agent (F12 §3.3).
/// En V1, seule la version « 1 » existe : il n'y a pas encore de N-1.
/// </summary>
public static class AgentContractVersionPolicy
{
    /// <summary>Version courante du contrat (portée par <c>Liakont.Agent.Contracts</c>).</summary>
    public static string Current => AgentContractVersion.ContractVersion;

    /// <summary>Version précédente encore supportée (N-1), ou <c>null</c> tant qu'elle n'existe pas.</summary>
    public static string? Previous => null;

    /// <summary>
    /// Indique si la version de contrat déclarée par l'agent est supportée. Une version absente,
    /// vide, inconnue ou antérieure à N-1 n'est PAS supportée (→ 426).
    /// </summary>
    public static bool IsSupported(string? contractVersion)
    {
        if (string.IsNullOrWhiteSpace(contractVersion))
        {
            return false;
        }

        var version = contractVersion.Trim();
        return string.Equals(version, Current, StringComparison.Ordinal)
            || (Previous is not null && string.Equals(version, Previous, StringComparison.Ordinal));
    }
}
