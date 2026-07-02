namespace Liakont.Host.Ged;

using System;
using System.Collections.Generic;
using Liakont.Modules.Ged.Contracts.Queries;

/// <summary>
/// Modèle assemblé de la fiche document GED (<c>/ged/document/{id}</c>, GED09b, F19 §6.7), alimenté par le
/// port de lecture <see cref="IGedDocumentQueries"/> (méta + axes + entités, masquage confidentiel déjà appliqué
/// server-side) et enrichi de l'INTÉGRITÉ (re-lecture du coffre vs <c>content_hash</c>) et de l'APERÇU
/// (<c>ReadableHtml</c> du paquet) via <c>Archive.Contracts</c>. Composition PURE en lecture — aucune règle
/// métier, aucun recalcul de montant (RL-22). Tenant-scopé par construction (la connexion EST le tenant).
/// </summary>
public sealed record GedDocumentDetailViewModel
{
    /// <summary>Identité GED du document.</summary>
    public required Guid Id { get; init; }

    /// <summary>Titre affiché (référence source brute).</summary>
    public required string Title { get; init; }

    /// <summary>Libellé métier libre du type de document, ou <c>null</c>.</summary>
    public string? DocKind { get; init; }

    /// <summary>Statut d'indexation brut (<c>draft|indexed|archived|deferred</c>) — la vue le traduit en français.</summary>
    public required string Status { get; init; }

    /// <summary>Classe de rétention brute (<c>legal_hold|tenant_bounded|erasable</c>) — la vue la traduit.</summary>
    public required string RetentionClass { get; init; }

    /// <summary>Motif humain (français) de déférement si <c>deferred</c>, sinon <c>null</c>.</summary>
    public string? DeferReason { get; init; }

    /// <summary>Verdict d'intégrité du document (re-lecture du coffre vs empreinte indexée, ou lien fiscal).</summary>
    public required GedDocumentIntegrityView Integrity { get; init; }

    /// <summary>Aperçu HTML lisible du paquet (<c>document-lisible.html</c>), ou <c>null</c> si absent. Rendu en bac à sable.</summary>
    public string? PreviewHtml { get; init; }

    /// <summary><c>true</c> si le document est rattaché à un document fiscal (soft-link <c>archive_entry_id</c> non nul).</summary>
    public bool IsFiscalLinked { get; init; }

    /// <summary>Soft-link vers le document fiscal (pour le lien « Voir la fiche fiscale »), ou <c>null</c>.</summary>
    public Guid? FiscalDocumentId { get; init; }

    /// <summary>Horodatage de création de la fiche d'index (UTC).</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Horodatage de dernière mise à jour de la fiche d'index (UTC), ou <c>null</c>.</summary>
    public DateTimeOffset? UpdatedUtc { get; init; }

    /// <summary>Valeurs d'axes courantes (confidentielles déjà exclues server-side).</summary>
    public required IReadOnlyList<GedManagedAxisValue> Axes { get; init; }

    /// <summary>Liens d'entités courants (types confidentiels déjà exclus server-side).</summary>
    public required IReadOnlyList<GedManagedEntityLink> Entities { get; init; }
}
