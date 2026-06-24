namespace Liakont.Modules.Pipeline.Contracts.Queries;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Lectures du journal d'émission e-reporting B2C de la marge (B4, <c>pipeline.b2c_margin_emissions</c>),
/// TENANT-SCOPÉES PAR CONSTRUCTION : elles s'exécutent sur la base DU TENANT courant (la connexion EST le
/// tenant — database-per-tenant, blueprint §7) ; aucune lecture cross-tenant n'est possible (CLAUDE.md n°9/17).
/// Le journal append-only PAR DOCUMENT est REGROUPÉ par lot d'émission (<c>emission_batch_id</c> : une
/// transmission = un POST) avec son état COURANT (dernière entrée) ; lecture seule, aucune (re)dérivation
/// fiscale (CLAUDE.md n°2). Consommé par la page console des émissions de marge B2C.
/// </summary>
public interface IB2cMarginEmissionQueries
{
    /// <summary>
    /// Émissions de la marge du tenant courant — une par transmission (lot d'émission, un POST) — avec leur
    /// état COURANT (dernière entrée). Optionnellement bornées à une période année-mois (<c>"yyyy-MM"</c>)
    /// appliquée sur le jour de l'agrégat : un filtre de DATE pur (jamais une règle fiscale). Une période vide
    /// ou nulle ne filtre pas. Triées par jour décroissant puis devise (déterminisme).
    /// </summary>
    Task<IReadOnlyList<B2cMarginEmissionAggregateDto>> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default);
}
