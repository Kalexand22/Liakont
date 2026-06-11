namespace Liakont.Modules.Reconciliation.Domain;

using System;

/// <summary>
/// Entrée de la FILE D'ATTENTE de réconciliation (item TRK07) : un PDF du pool et son sort.
/// Persistée dans la base du tenant (table mutable — un orphelin/une proposition peut être confirmé après
/// coup, contrairement à la piste d'audit append-only). La preuve définitive du rapprochement reste le
/// <c>DocumentEvent</c> (Documents) et l'addendum d'archive (WORM) ; cette file est l'état OPÉRATIONNEL,
/// consommée par la console (API04/WEB08). Une entrée est créée par PDF du pool découvert et traité.
/// </summary>
public sealed class ReconciliationQueueEntry
{
    private ReconciliationQueueEntry()
    {
    }

    /// <summary>Identifiant de l'entrée de file d'attente.</summary>
    public Guid Id { get; private set; }

    /// <summary>Identifiant stable du PDF dans le pool du tenant (clé d'unicité — un dépôt = une entrée).</summary>
    public string PoolPdfId { get; private set; } = string.Empty;

    /// <summary>Nom de fichier lisible du PDF du pool.</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>État du PDF dans la file (auto / proposition / orphelin / manuel).</summary>
    public ReconciliationStatus Status { get; private set; }

    /// <summary>Document rapproché ou proposé (<c>null</c> pour un orphelin).</summary>
    public Guid? ProposedDocumentId { get; private set; }

    /// <summary>Stratégie ayant produit la correspondance (<c>null</c> pour un orphelin).</summary>
    public MatchStrategy? Strategy { get; private set; }

    /// <summary>Confiance de la correspondance (<c>null</c> pour un orphelin).</summary>
    public MatchConfidence? Confidence { get; private set; }

    /// <summary>Motif/justification lisible (français, audit).</summary>
    public string Detail { get; private set; } = string.Empty;

    /// <summary>Horodatage de création de l'entrée (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>Horodatage de résolution — rapprochement effectif (<c>null</c> tant qu'en attente/orphelin).</summary>
    public DateTimeOffset? ResolvedUtc { get; private set; }

    /// <summary>Identité de l'opérateur ayant confirmé un rapprochement manuel (<c>null</c> sinon).</summary>
    public string? OperatorIdentity { get; private set; }

    /// <summary>Crée une entrée RAPPROCHÉE AUTOMATIQUEMENT (confiance haute, candidat unique).</summary>
    public static ReconciliationQueueEntry AutoReconciled(
        string poolPdfId,
        string fileName,
        Guid documentId,
        MatchStrategy strategy,
        string detail,
        DateTimeOffset nowUtc)
    {
        return new ReconciliationQueueEntry
        {
            Id = Guid.NewGuid(),
            PoolPdfId = RequirePoolPdfId(poolPdfId),
            FileName = RequireFileName(fileName),
            Status = ReconciliationStatus.ReconciledAuto,
            ProposedDocumentId = documentId,
            Strategy = strategy,
            Confidence = MatchConfidence.High,
            Detail = detail ?? string.Empty,
            CreatedUtc = nowUtc,
            ResolvedUtc = nowUtc,
            OperatorIdentity = null,
        };
    }

    /// <summary>Crée une PROPOSITION de confiance moyenne (candidat unique) en attente de confirmation opérateur.</summary>
    public static ReconciliationQueueEntry PendingProposal(
        string poolPdfId,
        string fileName,
        Guid documentId,
        string detail,
        DateTimeOffset nowUtc)
    {
        return new ReconciliationQueueEntry
        {
            Id = Guid.NewGuid(),
            PoolPdfId = RequirePoolPdfId(poolPdfId),
            FileName = RequireFileName(fileName),
            Status = ReconciliationStatus.PendingManual,
            ProposedDocumentId = documentId,
            Strategy = MatchStrategy.DateAndAmount,
            Confidence = MatchConfidence.Medium,
            Detail = detail ?? string.Empty,
            CreatedUtc = nowUtc,
            ResolvedUtc = null,
            OperatorIdentity = null,
        };
    }

    /// <summary>Crée un ORPHELIN (aucune correspondance ou ambiguïté) — file d'attente manuelle.</summary>
    public static ReconciliationQueueEntry Orphan(
        string poolPdfId,
        string fileName,
        string detail,
        DateTimeOffset nowUtc)
    {
        return new ReconciliationQueueEntry
        {
            Id = Guid.NewGuid(),
            PoolPdfId = RequirePoolPdfId(poolPdfId),
            FileName = RequireFileName(fileName),
            Status = ReconciliationStatus.Orphan,
            ProposedDocumentId = null,
            Strategy = null,
            Confidence = null,
            Detail = detail ?? string.Empty,
            CreatedUtc = nowUtc,
            ResolvedUtc = null,
            OperatorIdentity = null,
        };
    }

    /// <summary>Reconstitue une entrée depuis la persistance (lecture).</summary>
    public static ReconciliationQueueEntry Reconstitute(
        Guid id,
        string poolPdfId,
        string fileName,
        ReconciliationStatus status,
        Guid? proposedDocumentId,
        MatchStrategy? strategy,
        MatchConfidence? confidence,
        string detail,
        DateTimeOffset createdUtc,
        DateTimeOffset? resolvedUtc,
        string? operatorIdentity)
    {
        return new ReconciliationQueueEntry
        {
            Id = id,
            PoolPdfId = poolPdfId,
            FileName = fileName,
            Status = status,
            ProposedDocumentId = proposedDocumentId,
            Strategy = strategy,
            Confidence = confidence,
            Detail = detail,
            CreatedUtc = createdUtc,
            ResolvedUtc = resolvedUtc,
            OperatorIdentity = operatorIdentity,
        };
    }

    /// <summary>
    /// Confirme un rapprochement MANUEL par un opérateur : une PROPOSITION (confiance moyenne) ou un
    /// ORPHELIN devient <see cref="ReconciliationStatus.ReconciledManual"/>, rattaché au document
    /// <paramref name="documentId"/>. Lève si l'entrée est déjà rapprochée (un rapprochement ne se défait
    /// pas — le PDF est déjà en addendum WORM). L'identité de l'opérateur est obligatoire.
    /// </summary>
    public void ConfirmManually(Guid documentId, string operatorIdentity, string detail, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire pour une confirmation manuelle (TRK07).", nameof(operatorIdentity));
        }

        if (Status is ReconciliationStatus.ReconciledAuto or ReconciliationStatus.ReconciledManual)
        {
            throw new InvalidOperationException(
                $"Le PDF « {FileName} » est déjà rapproché (état {Status}) : un rapprochement archivé en WORM ne se défait pas (TRK07).");
        }

        Status = ReconciliationStatus.ReconciledManual;
        ProposedDocumentId = documentId;
        OperatorIdentity = operatorIdentity.Trim();
        Detail = detail ?? string.Empty;
        ResolvedUtc = nowUtc;
    }

    /// <summary>
    /// REJETTE une PROPOSITION de confiance moyenne : l'opérateur décline la correspondance proposée. Le
    /// PDF n'est PAS rapproché (aucun addendum WORM n'est créé) — il REDEVIENT un
    /// <see cref="ReconciliationStatus.Orphan"/> en file d'attente manuelle, où l'opérateur pourra le lier
    /// à un autre document (action « lier ») ou le laisser. Seule une PROPOSITION se rejette : un orphelin
    /// n'a aucune correspondance à décliner, et un rapprochement déjà effectué (auto/manuel) ne se défait
    /// pas (le PDF est en addendum WORM). L'identité de l'opérateur est obligatoire et conservée pour l'audit
    /// opérationnel.
    /// </summary>
    public void RejectProposal(string operatorIdentity, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire pour rejeter une proposition (API04/TRK07).", nameof(operatorIdentity));
        }

        if (Status != ReconciliationStatus.PendingManual)
        {
            throw new InvalidOperationException(
                $"Le PDF « {FileName} » n'est pas une proposition en attente (état {Status}) : seule une proposition de confiance moyenne peut être rejetée (TRK07).");
        }

        Status = ReconciliationStatus.Orphan;
        ProposedDocumentId = null;
        Strategy = null;
        Confidence = null;
        OperatorIdentity = operatorIdentity.Trim();
        Detail = $"Proposition rejetée par l'opérateur {operatorIdentity.Trim()} : PDF reclassé en orphelin pour rapprochement manuel (API04).";
    }

    private static string RequirePoolPdfId(string poolPdfId)
    {
        if (string.IsNullOrWhiteSpace(poolPdfId))
        {
            throw new ArgumentException("L'identifiant du PDF du pool est obligatoire.", nameof(poolPdfId));
        }

        return poolPdfId;
    }

    private static string RequireFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Le nom de fichier du PDF du pool est obligatoire.", nameof(fileName));
        }

        return fileName;
    }
}
