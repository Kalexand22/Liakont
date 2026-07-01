namespace Liakont.Modules.Ged.Contracts.Consultation;

using System;
using System.Collections.Generic;

/// <summary>
/// Une entrée de journal de consultation GED à écrire via <see cref="IConsultationAuditWriter"/> (F19 §6.6,
/// ADR-0036). Décrit UNE opération de lecture du portail. L'identité de l'acteur (<c>actor_id</c>) et la
/// corrélation par défaut sont résolues SERVER-SIDE par le writer depuis le contexte d'acteur (anti-spoof) ;
/// l'appelant fournit ici le contenu métier de la trace.
/// </summary>
/// <remarks>
/// Confidentialité (§6.5, anti-oracle) : le masquage server-side de <see cref="QueryText"/> et des valeurs
/// confidentielles de <see cref="Detail"/> est appliqué PAR LE WRITER. Le prédicat combine (a) le droit de
/// l'acteur <see cref="ActorHasConfidentialAccess"/> — calculé par l'appelant depuis les permissions, comme le
/// paramètre <c>@hasConfidentialRight</c> des requêtes de recherche (§6.2/§6.4) — et (b) la confidentialité
/// RÉELLE des axes/entités ciblés, que le writer résout depuis le catalogue (<c>ged_catalog</c>). Le défaut de
/// <see cref="ActorHasConfidentialAccess"/> est <see langword="false"/> (fail-safe : en cas d'oubli, on MASQUE
/// plutôt que de fuiter).
/// </remarks>
public sealed record ConsultationLogEntry
{
    /// <summary>Nature de l'opération de consultation (valeur fermée).</summary>
    public required ConsultationAction Action { get; init; }

    /// <summary>Document consulté, si l'action en cible un (<c>view_document</c>, ouverture de paquet).</summary>
    public Guid? ManagedDocumentId { get; init; }

    /// <summary>Entité explorée, si l'action en cible une (<c>explore_entity</c>).</summary>
    public Guid? EntityId { get; init; }

    /// <summary>Texte de la requête (recherche). Masqué/haché par le writer si un axe confidentiel est ciblé sans le droit.</summary>
    public string? QueryText { get; init; }

    /// <summary>Nombre de résultats retournés (recherche).</summary>
    public int? ResultCount { get; init; }

    /// <summary>
    /// Critères / facettes de l'opération, clés = <c>code</c> d'axe. La valeur d'une clé dont l'axe est
    /// confidentiel (sans le droit) est masquée par le writer avant insertion (sérialisée en <c>jsonb</c>).
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Detail { get; init; }

    /// <summary>
    /// Codes d'axes ciblés par l'opération (recherche/facette) en plus des clés de <see cref="Detail"/> ; sert au
    /// writer à décider le masquage de <see cref="QueryText"/>. Union avec les clés de <see cref="Detail"/>.
    /// </summary>
    public IReadOnlyCollection<string>? TargetedAxisCodes { get; init; }

    /// <summary>Code du type d'entité ciblé (<c>explore_entity</c>) ; sert au writer à décider le masquage.</summary>
    public string? TargetedEntityTypeCode { get; init; }

    /// <summary>
    /// L'acteur dispose-t-il du droit <c>liakont.ged.confidential</c> ? Calculé par l'appelant depuis les
    /// permissions (les permissions ne sont pas exposées par le contexte d'acteur socle). <see langword="false"/>
    /// par défaut (fail-safe : masquer en cas d'oubli).
    /// </summary>
    public bool ActorHasConfidentialAccess { get; init; }

    /// <summary>
    /// Corrélation reliant une recherche à ses ouvertures subséquentes. Si <see langword="null"/>, le writer
    /// retombe sur la corrélation de la requête courante (contexte d'acteur).
    /// </summary>
    public Guid? CorrelationId { get; init; }
}
