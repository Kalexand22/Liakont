namespace Liakont.Modules.Pipeline.Contracts;

using System;
using System.Collections.Generic;

/// <summary>
/// Détail d'une TRANSMISSION du journal d'émission e-reporting B2C (BUG-22, <c>pipeline.b2c_margin_emissions</c>)
/// : l'état COURANT du lot (dernière entrée) + le snapshot BRUT de la réponse PA (pour restitution lisible côté
/// console — jamais redérivé, CLAUDE.md n°2) + la liste des PIÈCES (documents) qui ont composé l'agrégat
/// (regroupées par <see cref="EmissionBatchId"/>). Lecture seule, tenant-scopée par construction. Aucun montant
/// (le journal n'en porte pas). Permet à l'opérateur de diagnostiquer un rejet et de remonter aux documents
/// sans ouvrir la base.
/// </summary>
public sealed record B2cMarginEmissionDetailDto
{
    /// <summary>Identité de la transmission (lot d'émission, un par POST).</summary>
    public required Guid EmissionBatchId { get; init; }

    /// <summary>Jour de l'agrégat (grain jour×devise, F03 §2.5).</summary>
    public required DateOnly AggregateDate { get; init; }

    /// <summary>Devise ISO 4217 de l'agrégat.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Code catégorie de transaction (TT-81, canonique — ex. <c>TMA1</c>).</summary>
    public required string Category { get; init; }

    /// <summary>Code rôle du déclarant (TT-15, canonique — ex. <c>SE</c>).</summary>
    public required string Role { get; init; }

    /// <summary>NOM du statut COURANT (dernière entrée) — ex. <c>Issued</c>, <c>Pending</c>, <c>RejectedByPa</c>, <c>Technical</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Identifiant serveur de la transaction côté PA (présent quand <c>Issued</c>), sinon <c>null</c>.</summary>
    public string? PaEmissionId { get; init; }

    /// <summary>Message opérateur (français) de la dernière issue non terminale (rejet/erreur), ou <c>null</c>.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Snapshot BRUT de la réponse PA (texte ou JSON), pour restitution LISIBLE côté console (jamais affiché brut —
    /// F10 §1) ; <c>null</c> si aucune réponse PA n'a été enregistrée pour le statut courant.
    /// </summary>
    public string? PaResponseSnapshot { get; init; }

    /// <summary>Horodatage de la dernière entrée de l'agrégat (dernière activité d'émission).</summary>
    public required DateTimeOffset LastActivityUtc { get; init; }

    /// <summary>Empreinte déterministe du CONTENU transmis (audit).</summary>
    public required string ContentHash { get; init; }

    /// <summary>Pièces (documents) ayant composé l'agrégat, distinctes, triées par référence source.</summary>
    public required IReadOnlyList<B2cMarginEmissionDocumentDto> Documents { get; init; }
}
