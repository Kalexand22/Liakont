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
    /// vide, inconnue ou antérieure à N-1 n'est PAS supportée (→ 426). Délègue à la décision pure
    /// <see cref="IsSupported(string?, string, string?)"/> avec la matrice live (<see cref="Current"/>,
    /// <see cref="Previous"/>).
    /// </summary>
    public static bool IsSupported(string? contractVersion) => IsSupported(contractVersion, Current, Previous);

    /// <summary>
    /// Décision PURE de support de version pour une matrice (N, N-1) arbitraire : une version est
    /// supportée si elle vaut <paramref name="current"/> (N) ou <paramref name="previous"/> (N-1, si
    /// non nul) ; une version absente, vide, inconnue ou antérieure à N-1 ne l'est pas (→ 426).
    ///
    /// <para>Cette surcharge matérialise le seam de cohabitation N/N-1 (RDF08, ADR-0001/F12 §6.4) :
    /// elle rend la branche N-1 — jamais exercée par la matrice live tant que <see cref="Previous"/>
    /// vaut <c>null</c> — testable AVANT la première rupture de contrat, sans fausser la politique
    /// servie en production. La logique 426 reste la même fonction des deux côtés.</para>
    /// </summary>
    internal static bool IsSupported(string? contractVersion, string current, string? previous)
    {
        if (string.IsNullOrWhiteSpace(contractVersion))
        {
            return false;
        }

        var version = contractVersion.Trim();
        return string.Equals(version, current, StringComparison.Ordinal)
            || (previous is not null && string.Equals(version, previous, StringComparison.Ordinal));
    }
}
