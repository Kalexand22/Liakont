namespace Liakont.Modules.Documents.Domain.Entities;

using System;

/// <summary>
/// Document métier de la passerelle (F06 §3, item TRK01). Agrégat racine du module <c>Documents</c> :
/// il porte l'état du document dans la passerelle, ses montants de contrôle (en <see cref="decimal"/>,
/// CLAUDE.md n°1) et l'empreinte du payload pivot. Il vit dans la base DU TENANT (database-per-tenant,
/// blueprint §7) : aucune colonne de tenant n'est nécessaire, l'isolation est ASSURÉE PAR LA CONNEXION
/// (la connexion EST le tenant — F06 amendement stockage 2026-06-03). Le document est créé en état
/// <see cref="DocumentState.Detected"/> par l'ingestion (PIV04) ; la machine à états arrive avec TRK02.
/// </summary>
/// <remarks>
/// Les montants sont ceux CALCULÉS PAR LA SOURCE (portés par le pivot) — le module Documents ne calcule
/// ni ne valide rien (frontière module-rules §2 ; le contrôle des totaux est dans Validation, F04). Le
/// <see cref="DocumentType"/> est le type BRUT de la source (la classification facture/avoir vit dans
/// Validation — ADR-0004 D3-3). Tout champ source absent reste <c>null</c> (jamais de défaut implicite
/// masquant une donnée manquante — blueprint §8).
/// </remarks>
public sealed class Document
{
    private Document()
    {
    }

    /// <summary>Identifiant du document, attribué par l'ingestion et partagé avec la réception et l'événement (PIV04).</summary>
    public Guid Id { get; private set; }

    /// <summary>Référence du document dans le système source (réconciliation + audit).</summary>
    public string SourceReference { get; private set; } = string.Empty;

    /// <summary>Numéro du document (EN 16931 BT-1) — clé fonctionnelle vers la PA. La source est le seul créateur de numéros.</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>Type de document BRUT porté par la source (classification facture/avoir déléguée à Validation — ADR-0004 D3-3).</summary>
    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>Date d'émission (EN 16931 BT-2).</summary>
    public DateOnly IssueDate { get; private set; }

    /// <summary>SIREN du fournisseur/émetteur (EN 16931 BT-30). Absent dans la source = <c>null</c>.</summary>
    public string? SupplierSiren { get; private set; }

    /// <summary>Raison sociale / nom du destinataire (B2C sans tiers identifié = <c>null</c>).</summary>
    public string? CustomerName { get; private set; }

    /// <summary>Indice BRUT « société » porté par la source pour le destinataire (interprétation déléguée à Validation, VAL05).</summary>
    public bool CustomerIsCompanyHint { get; private set; }

    /// <summary>Total HT (EN 16931 BT-109), <see cref="decimal"/>.</summary>
    public decimal TotalNet { get; private set; }

    /// <summary>Total TVA (EN 16931 BT-110), <see cref="decimal"/>.</summary>
    public decimal TotalTax { get; private set; }

    /// <summary>Total TTC (EN 16931 BT-112), <see cref="decimal"/>.</summary>
    public decimal TotalGross { get; private set; }

    /// <summary>État du document dans la passerelle (F06 §3).</summary>
    public DocumentState State { get; private set; }

    /// <summary>Empreinte canonique du payload pivot (SHA-256 hex) — anti-doublon (TRK03) et détection d'altération (F06).</summary>
    public string PayloadHash { get; private set; } = string.Empty;

    /// <summary>Identifiant du document côté Plateforme Agréée (renseigné à l'émission — Transmission). Absent = <c>null</c>.</summary>
    public string? PaDocumentId { get; private set; }

    /// <summary>Version de table de mapping TVA appliquée (F03), renseignée par le pipeline (PIP). Absent = <c>null</c>.</summary>
    public string? MappingVersion { get; private set; }

    /// <summary>Première observation du document (UTC).</summary>
    public DateTimeOffset FirstSeenUtc { get; private set; }

    /// <summary>Dernière mise à jour du document (UTC).</summary>
    public DateTimeOffset LastUpdateUtc { get; private set; }

    /// <summary>
    /// Crée un document en état <see cref="DocumentState.Detected"/> à partir des données d'un document
    /// reçu par l'ingestion (PIV04). Les montants sont conservés tels que calculés par la source.
    /// </summary>
    public static Document CreateDetected(
        Guid id,
        string sourceReference,
        string documentNumber,
        string documentType,
        DateOnly issueDate,
        string? supplierSiren,
        string? customerName,
        bool customerIsCompanyHint,
        decimal totalNet,
        decimal totalTax,
        decimal totalGross,
        string payloadHash,
        DateTimeOffset detectedAtUtc)
    {
        Require(sourceReference, nameof(sourceReference), "La référence source est obligatoire.");
        Require(documentNumber, nameof(documentNumber), "Le numéro de document est obligatoire (EN 16931 BT-1).");
        Require(documentType, nameof(documentType), "Le type de document source est obligatoire.");
        Require(payloadHash, nameof(payloadHash), "L'empreinte du payload est obligatoire.");

        return new Document
        {
            Id = id,
            SourceReference = sourceReference.Trim(),
            DocumentNumber = documentNumber.Trim(),
            DocumentType = documentType.Trim(),
            IssueDate = issueDate,
            SupplierSiren = NullIfBlank(supplierSiren),
            CustomerName = NullIfBlank(customerName),
            CustomerIsCompanyHint = customerIsCompanyHint,
            TotalNet = totalNet,
            TotalTax = totalTax,
            TotalGross = totalGross,
            State = DocumentState.Detected,
            PayloadHash = payloadHash.Trim(),
            PaDocumentId = null,
            MappingVersion = null,
            FirstSeenUtc = detectedAtUtc,
            LastUpdateUtc = detectedAtUtc,
        };
    }

    /// <summary>
    /// Reconstitue un document depuis la persistance (lecture). Aucune logique de transition n'est
    /// appliquée : la machine à états (TRK02) opère sur l'agrégat reconstitué.
    /// </summary>
    public static Document Reconstitute(
        Guid id,
        string sourceReference,
        string documentNumber,
        string documentType,
        DateOnly issueDate,
        string? supplierSiren,
        string? customerName,
        bool customerIsCompanyHint,
        decimal totalNet,
        decimal totalTax,
        decimal totalGross,
        DocumentState state,
        string payloadHash,
        string? paDocumentId,
        string? mappingVersion,
        DateTimeOffset firstSeenUtc,
        DateTimeOffset lastUpdateUtc)
    {
        return new Document
        {
            Id = id,
            SourceReference = sourceReference,
            DocumentNumber = documentNumber,
            DocumentType = documentType,
            IssueDate = issueDate,
            SupplierSiren = supplierSiren,
            CustomerName = customerName,
            CustomerIsCompanyHint = customerIsCompanyHint,
            TotalNet = totalNet,
            TotalTax = totalTax,
            TotalGross = totalGross,
            State = state,
            PayloadHash = payloadHash,
            PaDocumentId = paDocumentId,
            MappingVersion = mappingVersion,
            FirstSeenUtc = firstSeenUtc,
            LastUpdateUtc = lastUpdateUtc,
        };
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void Require(string value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }
    }
}
