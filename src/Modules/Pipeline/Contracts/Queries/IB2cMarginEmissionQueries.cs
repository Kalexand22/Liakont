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
    /// Lot d'émission dans lequel ce DOCUMENT a été RÉELLEMENT déclaré (statut <c>Issued</c>) — sa transmission
    /// agrégée e-reporting B2C (BUG-24). <c>null</c> si le document n'a pas (encore) été e-reporté avec succès
    /// (jamais émis, ou seulement tenté/rejeté). Permet à la fiche détail de REFLÉTER l'état d'e-reporting au
    /// read-time (lien doc ↔ lot d'émission) SANS toucher la machine à états du document : un document e-reporté
    /// reste techniquement « prêt à l'envoi » côté domaine, mais la voie document ne le concerne plus (garde D1).
    /// Si le document figure dans plusieurs émissions Issued (document tardif → nouvel agrégat), la PLUS RÉCENTE
    /// prime. Tenant-scopé par construction (la connexion EST le tenant).
    /// </summary>
    Task<Guid?> GetIssuedEmissionBatchForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
