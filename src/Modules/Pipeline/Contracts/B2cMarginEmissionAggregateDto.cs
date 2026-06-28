namespace Liakont.Modules.Pipeline.Contracts;

using System;

/// <summary>
/// Vue de lecture d'une TRANSMISSION du journal d'émission e-reporting B2C de la marge (flux 10.3, B4 —
/// <c>pipeline.b2c_margin_emissions</c>). Le journal est append-only AU GRAIN DOCUMENT (attempt-once : une
/// entrée <c>Pending</c> avant le POST, puis l'issue, par document) ; cette vue REGROUPE par lot d'émission
/// (<see cref="EmissionBatchId"/> = une transmission réelle, un POST) et expose l'état COURANT (la dernière
/// entrée) de chaque transmission. Aucun montant n'est exposé (le journal n'en porte pas — CLAUDE.md n°2) : la
/// vue trace le CYCLE d'émission (Pending → Émis + id PA), pas une forme fiscale.
/// </summary>
public sealed record B2cMarginEmissionAggregateDto
{
    /// <summary>Identité de la transmission (lot d'émission, un par POST) — clé de regroupement de la vue.</summary>
    public required Guid EmissionBatchId { get; init; }

    /// <summary>Jour de l'agrégat (grain jour×devise, F03 §2.5).</summary>
    public required DateOnly AggregateDate { get; init; }

    /// <summary>Devise ISO 4217 de l'agrégat.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Code catégorie de transaction (TT-81, canonique — ex. <c>TMA1</c>).</summary>
    public required string Category { get; init; }

    /// <summary>Code rôle du déclarant (TT-15, canonique — ex. <c>SE</c>).</summary>
    public required string Role { get; init; }

    /// <summary>Nombre de pièces (documents) ayant contribué à cet agrégat.</summary>
    public required int DocumentCount { get; init; }

    /// <summary>NOM du statut COURANT (dernière entrée de l'agrégat) — ex. <c>Issued</c>, <c>Pending</c>, <c>RejectedByPa</c>, <c>Technical</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Identifiant serveur de la transaction côté PA (présent quand le statut courant est <c>Issued</c>), sinon <c>null</c>.</summary>
    public string? PaEmissionId { get; init; }

    /// <summary>Message opérateur (français) de la dernière issue non terminale (rejet/erreur), ou <c>null</c>.</summary>
    public string? Detail { get; init; }

    /// <summary>Horodatage de la dernière entrée de l'agrégat (dernière activité d'émission).</summary>
    public required DateTimeOffset LastActivityUtc { get; init; }

    /// <summary>Empreinte déterministe du CONTENU transmis (audit) — non unique par transmission (deux POST d'un même contenu la partagent).</summary>
    public required string ContentHash { get; init; }
}
