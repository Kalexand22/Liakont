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
    public ManagedDocument(
        Guid id,
        string title,
        string? docKind,
        string status,
        string retentionClass = DefaultRetentionClass,
        string? deferReason = null)
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
}
