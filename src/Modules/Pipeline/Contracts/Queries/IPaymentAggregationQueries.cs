namespace Liakont.Modules.Pipeline.Contracts.Queries;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Lectures de la projection des agrégats jour×taux de l'e-reporting de paiement (PIP03a,
/// <c>pipeline.payment_aggregations</c>), TENANT-SCOPÉES PAR CONSTRUCTION : elles s'exécutent sur la base
/// DU TENANT courant (la connexion EST le tenant — database-per-tenant, blueprint §7) ; aucune lecture
/// cross-tenant n'est possible (CLAUDE.md n°9/17). Consommé par <c>GET /payments</c> (API01b) et la page
/// Encaissements (WEB06). La qualification fiscale (statut) est calculée par PIP03a et seulement EXPOSÉE ici.
/// </summary>
public interface IPaymentAggregationQueries
{
    /// <summary>
    /// Agrégats jour×taux du tenant courant, optionnellement bornés à une période année-mois
    /// (<c>"yyyy-MM"</c>) appliquée sur le jour d'encaissement — un filtre de DATE (jamais une règle
    /// fiscale). Une période vide ou nulle ne filtre pas. Triés par jour puis taux (déterminisme).
    /// </summary>
    Task<IReadOnlyList<PaymentDailyAggregateDto>> GetAggregationsAsync(string? period, CancellationToken cancellationToken = default);
}
