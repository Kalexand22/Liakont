namespace Liakont.Modules.Pipeline.Contracts.Queries;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Lectures du registre de la marge à déclarer (<c>pipeline.margin_registry</c>, Livrable 2), TENANT-SCOPÉES PAR
/// CONSTRUCTION : elles s'exécutent sur la base DU TENANT courant (la connexion EST le tenant — database-per-tenant,
/// blueprint §7) ; aucune lecture cross-tenant n'est possible (CLAUDE.md n°9/17). Lecture seule, aucune
/// (re)dérivation fiscale (CLAUDE.md n°2). Consommé par la page console « TVA / Déclaration ».
/// </summary>
public interface IMarginRegistryQueries
{
    /// <summary>
    /// Agrégats mois × devise × taux du registre de marge du tenant courant (somme des bases HT + TVA sur marge),
    /// optionnellement bornés à une période année-mois (<c>"yyyy-MM"</c>) appliquée sur le jour d'émission — un
    /// filtre de DATE pur (jamais une règle fiscale). Une période vide ou nulle ne filtre pas. Triés par mois
    /// décroissant puis devise et taux (déterminisme).
    /// </summary>
    Task<IReadOnlyList<MarginRegistryMonthlyDto>> GetMonthlyAsync(string? period, CancellationToken cancellationToken = default);
}
