namespace Liakont.Modules.Payments.Contracts.Queries;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Payments.Contracts.DTOs;

/// <summary>
/// Lectures du module Payments pour l'API/console (item TRK04). Toutes les requêtes sont TENANT-SCOPÉES PAR
/// CONSTRUCTION : elles s'exécutent sur la base DU TENANT courant (la connexion EST le tenant —
/// database-per-tenant, blueprint §7) ; aucune requête cross-tenant n'est possible (CLAUDE.md n°9/17).
/// </summary>
public interface IPaymentQueries
{
    /// <summary>Paiement par identifiant, ou <c>null</c> s'il n'existe pas dans ce tenant.</summary>
    Task<PaymentDto?> GetPaymentByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Agrégat de paiement par identifiant, ou <c>null</c>.</summary>
    Task<PaymentAggregateDto?> GetAggregateByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Piste d'audit complète d'un agrégat de paiement (ordre chronologique).</summary>
    Task<IReadOnlyList<PaymentAggregateEventDto>> GetAggregateEventsAsync(Guid aggregateId, CancellationToken cancellationToken = default);
}
