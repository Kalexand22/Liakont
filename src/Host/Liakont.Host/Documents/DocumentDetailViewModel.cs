namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using Liakont.Modules.Documents.Contracts.DTOs;

/// <summary>
/// Modèle assemblé de la page détail document (WEB03a, F10 §2.3), alimenté par le module Documents (TRK01)
/// via <see cref="Liakont.Modules.Documents.Contracts.Queries.IDocumentQueries"/>. Composition pure des DTO
/// de contrat — AUCUNE règle métier (la classification fiscale, la machine à états et la validation restent
/// dans leurs modules) : seule une PRÉSENTATION en lecture de la piste d'audit (motif de blocage courant,
/// présence d'archive) est dérivée ici, à l'identique de l'endpoint <c>GET /api/v1/documents/{id}</c>.
/// Tenant-scopé par construction (la connexion EST le tenant — database-per-tenant, blueprint §7).
/// </summary>
public sealed record DocumentDetailViewModel
{
    /// <summary>En-tête du document tel que lu (n°, date, acheteur, totaux, état). Jamais <c>null</c>.</summary>
    public required DocumentDto Document { get; init; }

    /// <summary>Piste d'audit complète (ordre chronologique), pour l'onglet Historique.</summary>
    public required IReadOnlyList<DocumentEventDto> Events { get; init; }

    /// <summary>
    /// Contenu affichable du document tel que transmis (onglet Contenu, FIX205 / F10 §2.3) : lignes, charges/remises
    /// de niveau document, et contrôle de cohérence des totaux, projeté depuis le pivot du dernier événement d'envoi
    /// (<see cref="DocumentLineProjection.FromTransmittedSnapshot"/>). Vide quand le document n'a pas encore été
    /// transmis (la vue affiche alors une note, jamais une ligne ou un verdict inventés).
    /// </summary>
    public DocumentContentView Content { get; init; } = DocumentContentView.Empty;

    /// <summary>
    /// Motif de blocage courant (dernier événement <c>DocumentBlocked</c>) UNIQUEMENT quand le document est
    /// actuellement <c>Blocked</c>, sinon <c>null</c> : un motif périmé sur un document débloqué/émis serait
    /// un message opérateur trompeur (CLAUDE.md n°12). L'historique complet reste dans <see cref="Events"/>.
    /// </summary>
    public string? BlockingReason { get; init; }

    /// <summary>
    /// Récapitulatif de marge (onglet Contenu) quand le document est au régime de la marge (B2C ou B2B, art. 297 E) :
    /// commission acheteur + vendeur, base HT, TVA sur marge à déclarer. <c>null</c> hors régime de la marge. Calculé
    /// par le module Pipeline (cœurs e-reporting réutilisés) — ici pure PRÉSENTATION, aucune fiscalité dérivée.
    /// </summary>
    public MarginRecapView? MarginRecap { get; init; }

    /// <summary>Référence d'archive WORM du document (onglet Archive), ou <c>null</c> s'il n'est pas archivé.</summary>
    public ArchiveReferenceDto? Archive { get; init; }

    /// <summary><c>true</c> si une entrée de coffre existe pour ce document, <c>false</c> sinon.</summary>
    public bool IsArchived { get; init; }
}
