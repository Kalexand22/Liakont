namespace Liakont.Modules.Pipeline.Contracts.Queries;

using System;
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

    /// <summary>
    /// Détail d'UNE transmission (lot d'émission, BUG-22) : son état COURANT (dernière entrée) + le snapshot
    /// BRUT de réponse PA + la liste des PIÈCES qui l'ont composée (documents distincts du lot). <c>null</c> si
    /// le lot est introuvable (autre tenant, ou identifiant inconnu). Tenant-scopé par construction.
    /// </summary>
    Task<B2cMarginEmissionDetailDto?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lot d'émission (une transmission = un POST) auquel APPARTIENT un document e-reporté : la DERNIÈRE
    /// transmission <c>Issued</c> qui l'a inclus (<c>emission_batch_id</c>). <c>null</c> si le document n'a jamais
    /// été e-reporté avec succès (aucune entrée <c>Issued</c>). C'est la SOURCE DE VÉRITÉ de la liaison
    /// document→lot : elle existe pour TOUT document e-reporté — qu'il l'ait été par le job (frais) OU rétro-corrigé
    /// par le backfill V012 (aucun événement d'audit requis, contrairement au journal du document). Consommée par le
    /// lien « Voir la déclaration » de la fiche détail (BUG-24, ADR-0037 §4). Tenant-scopée par construction.
    /// </summary>
    Task<Guid?> GetEmissionBatchIdForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dernière émission <c>Issued</c> d'un document (lot + référence source), ou <c>null</c> si le document n'a
    /// jamais été e-reporté avec succès. Sert au RATTRAPAGE de l'état résiduel « émission acceptée mais document
    /// resté ReadyToSend » (ADR-0037 D3) : rejouer le gel du lien reporting↔pièce (D2, via la
    /// <c>SourceReference</c>) ET la transition d'état, sans re-transmission. Tenant-scopée par construction.
    /// </summary>
    Task<B2cResidualEmissionDto?> GetResidualIssuedEmissionForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
