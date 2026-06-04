namespace Liakont.Modules.Archive.Contracts;

using System;

/// <summary>
/// Demande de création d'un paquet d'archive pour un document ÉMIS (TRK05 §2). Portée par le port
/// <see cref="IArchiveService"/>, consommée par le pipeline (PIP) à l'entrée en état <c>Issued</c> du
/// document. Le coffre archive EXACTEMENT ce qui a été transmis (<see cref="PayloadJson"/>) et la réponse
/// de la PA (<see cref="PaResponseJson"/>) ; il ne recompose ni ne reclasse rien.
///
/// Pièces optionnelles pilotées par CAPACITÉS (jamais un <c>if</c> sur un PA/adaptateur concret) :
/// la facture légale PA n'est jointe que si la PA déclare <c>SupportsDocumentRetrieval</c> ; le bordereau
/// source que si l'adaptateur déclare <c>ProvidesSourceDocuments</c>. Quand une pièce est absente, son
/// motif est OBLIGATOIRE et tracé dans le manifest (jamais une absence silencieuse).
/// </summary>
public sealed class ArchivePackageRequest
{
    /// <summary>Identifiant du document (FK <c>documents.documents</c>).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Numéro du document (clé d'arborescence du paquet).</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Date d'émission (année/mois de l'arborescence du paquet).</summary>
    public required DateOnly IssueDate { get; init; }

    /// <summary>Le payload EXACT transmis à la PA (sérialisé). Archivé tel quel en <c>payload.json</c>.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>La réponse BRUTE de la PA + identifiants DGFiP. Archivée telle quelle en <c>reponse-pa.json</c>.</summary>
    public required string PaResponseJson { get; init; }

    /// <summary>Données du rendu lisible autonome (<c>document-lisible.html</c>).</summary>
    public required ArchiveReadableDocument Readable { get; init; }

    /// <summary>Trace de mapping TVA (JSON) appliquée — tracée dans le manifest, ou <c>null</c>.</summary>
    public string? MappingTraceJson { get; init; }

    /// <summary>Facture légale générée par la PA (Factur-X/UBL), ou <c>null</c> si la PA ne la fournit pas.</summary>
    public ArchiveAttachment? PaInvoice { get; init; }

    /// <summary>Motif d'absence de la facture PA (obligatoire quand <see cref="PaInvoice"/> est <c>null</c>).</summary>
    public string? PaInvoiceAbsenceReason { get; init; }

    /// <summary>Bordereau émis par le logiciel source, ou <c>null</c> si l'adaptateur ne le fournit pas.</summary>
    public ArchiveAttachment? SourceDocument { get; init; }

    /// <summary>Motif d'absence du bordereau source (obligatoire quand <see cref="SourceDocument"/> est <c>null</c>).</summary>
    public string? SourceDocumentAbsenceReason { get; init; }
}
