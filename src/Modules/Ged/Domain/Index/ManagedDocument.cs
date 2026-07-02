namespace Liakont.Modules.Ged.Domain.Index;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Entité-pivot d'INDEX à UPSERT dans <c>ged_index.managed_documents</c> (F19 §3.4.1, item GED05b). Son identité
/// primaire est SA PROPRE clé GED (<see cref="Id"/>, attribuée par le handler d'ingestion et portée dans
/// <c>ManagedDocumentReceivedV1</c>) — le rattachement fiscal est l'EXCEPTION, jamais posée par ce canal
/// (<see cref="FiscalDocumentId"/>/<see cref="ArchiveEntryId"/> restent <see langword="null"/> ; ils arrivent à
/// l'archivage GED07 / au pont fiscal). Le consommateur l'insère avec son STATUT FINAL (<c>indexed</c> mappé,
/// <c>deferred</c> non mappable) — l'insert direct au bon statut évite une mutation (donc aucune écriture au
/// <c>managed_document_change_log</c>) ; l'idempotence de replay repose sur <c>ON CONFLICT (id) DO NOTHING</c> et le
/// statut lu sous garde (RL-04). <c>content_hash</c> reste posé UNE SEULE FOIS à l'archivage (GED07), jamais ici.
/// </summary>
public sealed class ManagedDocument
{
    /// <summary>Classe de rétention par défaut à l'ingestion GED (document métier borné au tenant).</summary>
    public const string DefaultRetentionClass = "tenant_bounded";

    /// <summary>Statuts autorisés, miroir Domain de <c>ck_md_status</c>.</summary>
    public static readonly IReadOnlyList<string> AllowedStatuses = ["draft", "indexed", "archived", "deferred"];

    /// <summary>Classes de rétention autorisées, miroir Domain de <c>ck_md_retention</c> (F19 §7).</summary>
    public static readonly IReadOnlyList<string> AllowedRetentionClasses = ["legal_hold", "tenant_bounded", "erasable"];

    /// <summary>Crée l'entité-pivot d'index à upserter. <paramref name="title"/> obligatoire (contrainte NOT NULL).</summary>
    /// <remarks>
    /// Les liens souples (<paramref name="fiscalDocumentId"/>/<paramref name="archiveEntryId"/>/
    /// <paramref name="archivePath"/>/<paramref name="contentHash"/>) sont OPTIONNELS et par défaut <see langword="null"/> :
    /// le canal d'ingestion GED (GED05b) n'en pose AUCUN (l'identité primaire est la clé GED). Ils sont renseignés par le
    /// backfill rétroactif du corpus fiscal déjà scellé (GED10, F19 §10/§11 D12) : soft-links LOGIQUES vers
    /// <c>documents.documents</c>/<c>documents.archive_entries</c> (aucune FK cross-schéma, F19 §3.4.1). Additifs :
    /// ne changent RIEN au comportement des appelants existants (colonnes déjà présentes en base, V008).
    /// </remarks>
    public ManagedDocument(
        Guid id,
        string title,
        string? docKind,
        string status,
        string retentionClass = DefaultRetentionClass,
        string? deferReason = null,
        Guid? fiscalDocumentId = null,
        Guid? archiveEntryId = null,
        string? archivePath = null,
        string? contentHash = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identité d'un ManagedDocument est obligatoire (attribuée à l'ingestion).", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (!AllowedStatuses.Contains(status, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Statut de ManagedDocument « {status} » invalide : attendu l'une de [{string.Join(", ", AllowedStatuses)}] "
                    + "(miroir ck_md_status).",
                nameof(status));
        }

        if (!AllowedRetentionClasses.Contains(retentionClass, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Classe de rétention « {retentionClass} » invalide : attendu l'une de [{string.Join(", ", AllowedRetentionClasses)}] "
                    + "(miroir ck_md_retention).",
                nameof(retentionClass));
        }

        Id = id;
        Title = title;
        DocKind = docKind;
        Status = status;
        RetentionClass = retentionClass;
        DeferReason = deferReason;
        FiscalDocumentId = fiscalDocumentId;
        ArchiveEntryId = archiveEntryId;
        ArchivePath = archivePath;
        ContentHash = contentHash;
    }

    /// <summary>Identité GED du document (clé primaire, attribuée à l'ingestion, portée par l'événement).</summary>
    public Guid Id { get; }

    /// <summary>Titre affiché (NOT NULL) — à l'ingestion GED = référence source (valeur BRUTE, jamais inventée).</summary>
    public string Title { get; }

    /// <summary>Libellé métier libre (PAS un état fiscal) — à l'ingestion GED = type de document source.</summary>
    public string? DocKind { get; }

    /// <summary>Statut d'indexation (<c>indexed</c> mappé / <c>deferred</c> non mappable).</summary>
    public string Status { get; }

    /// <summary>Classe de rétention (F19 §7).</summary>
    public string RetentionClass { get; }

    /// <summary>Motif humain de déférement (français, actionnable), ou <see langword="null"/> si le document est indexé.</summary>
    public string? DeferReason { get; }

    /// <summary>Soft-link LOGIQUE vers <c>documents.documents.id</c> (backfill fiscal GED10), ou <see langword="null"/> (canal GED pur).</summary>
    public Guid? FiscalDocumentId { get; }

    /// <summary>Soft-link LOGIQUE vers <c>documents.archive_entries.id</c> — clé d'idempotence du backfill (GED10), ou <see langword="null"/>.</summary>
    public Guid? ArchiveEntryId { get; }

    /// <summary>Chemin du paquet WORM déjà scellé (chaîne fiscale <c>{année}/…</c> ou <c>_ged/…</c>), ou <see langword="null"/>.</summary>
    public string? ArchivePath { get; }

    /// <summary>Empreinte du paquet déjà scellé (recopiée du coffre, jamais recalculée ici — GED10), ou <see langword="null"/>.</summary>
    public string? ContentHash { get; }
}
