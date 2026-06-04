namespace Liakont.Modules.Documents.Contracts.Queries;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;

/// <summary>
/// Lectures du module Documents pour l'API/console (item TRK01). Toutes les requêtes sont
/// TENANT-SCOPÉES PAR CONSTRUCTION : elles s'exécutent sur la base DU TENANT courant (la connexion EST
/// le tenant — database-per-tenant, blueprint §7) ; aucune requête cross-tenant n'est possible
/// (CLAUDE.md n°9/17).
/// </summary>
public interface IDocumentQueries
{
    /// <summary>Document par identifiant, ou <c>null</c> s'il n'existe pas dans ce tenant.</summary>
    Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Document par numéro (EN 16931 BT-1) dans le tenant, ou <c>null</c>. Le numéro n'est pas unique en
    /// base (un document rejeté peut être remplacé sous un nouveau numéro, l'ancien passant Superseded —
    /// F06 §4, mécanique TRK02/TRK03) : cette lecture retourne le document le PLUS RÉCENT pour ce numéro.
    /// </summary>
    Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Documents d'un état donné, paginés (file d'attente console). <paramref name="page"/> est 1-basé ;
    /// <paramref name="pageSize"/> est borné par l'implémentation. Triés par dernière mise à jour décroissante.
    /// </summary>
    Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(
        string state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Piste d'audit complète d'un document (ordre chronologique).</summary>
    Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default);
}
