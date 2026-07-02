namespace Liakont.Modules.Ged.Contracts.Queries;

using System;
using System.Collections.Generic;

/// <summary>
/// Vue de LECTURE d'un document géré pour la fiche <c>/ged/document/{id}</c> (F19 §6.7, GED09b) : les
/// méta-colonnes de <c>managed_documents</c> (§3.4.1) + ses valeurs d'axes COURANTES + ses liens d'entités
/// COURANTS, restitués tenant-scopé (la connexion EST le tenant). Les axes/entités confidentiels sont exclus
/// server-side selon le droit de l'acteur (§6.5). Les rattachements fiscaux (<see cref="FiscalDocumentId"/>/
/// <see cref="ArchiveEntryId"/>) sont des SOFT-LINKS projetés à la lecture, JAMAIS des copies de champs fiscaux
/// (RL-22). L'intégrité et l'aperçu ne vivent PAS ici : ils sont assemblés par la couche de présentation depuis
/// le coffre (<c>Archive.Contracts</c>) à partir de <see cref="ArchivePath"/>/<see cref="ContentHash"/>.
/// </summary>
public sealed record GedManagedDocumentView
{
    /// <summary>Identité GED du document (clé primaire).</summary>
    public required Guid Id { get; init; }

    /// <summary>Titre affiché (référence source brute, jamais inventée).</summary>
    public required string Title { get; init; }

    /// <summary>Libellé métier libre du type de document source, ou <see langword="null"/>.</summary>
    public string? DocKind { get; init; }

    /// <summary>Statut d'indexation (<c>draft|indexed|archived|deferred</c>).</summary>
    public required string Status { get; init; }

    /// <summary>Classe de rétention (F19 §7 : <c>legal_hold|tenant_bounded|erasable</c>).</summary>
    public required string RetentionClass { get; init; }

    /// <summary>Motif humain (français) de déférement si le document est <c>deferred</c>, sinon <see langword="null"/>.</summary>
    public string? DeferReason { get; init; }

    /// <summary>Soft-link vers <c>documents.documents.id</c> (backfill fiscal GED10), ou <see langword="null"/> (document GED pur).</summary>
    public Guid? FiscalDocumentId { get; init; }

    /// <summary>Soft-link vers <c>documents.archive_entries.id</c> (paquet fiscal scellé), ou <see langword="null"/>.</summary>
    public Guid? ArchiveEntryId { get; init; }

    /// <summary>Chemin du manifest du paquet dans le coffre (<c>_ged/…</c> ou fiscal), ou <see langword="null"/> si non archivé.</summary>
    public string? ArchivePath { get; init; }

    /// <summary>Empreinte indexée du paquet (copie du coffre, jamais recalculée en base), ou <see langword="null"/>.</summary>
    public string? ContentHash { get; init; }

    /// <summary>Horodatage de création de la fiche d'index (UTC).</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Horodatage de dernière mise à jour de la fiche d'index (UTC), ou <see langword="null"/>.</summary>
    public DateTimeOffset? UpdatedUtc { get; init; }

    /// <summary>Valeurs d'axes courantes (confidentielles exclues sans le droit, §6.5).</summary>
    public required IReadOnlyList<GedManagedAxisValue> Axes { get; init; }

    /// <summary>Liens d'entités courants (types confidentiels exclus sans le droit, §6.5).</summary>
    public required IReadOnlyList<GedManagedEntityLink> Entities { get; init; }
}
