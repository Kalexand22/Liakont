namespace Liakont.OnSiteSignature.Client;

using System;

/// <summary>
/// Objet IMMUABLE posté au proxy plateforme <c>OnSiteCapture</c> (ADR-0030 §3). Pur transport HTTP : le
/// client ne porte AUCUNE logique métier et n'envoie PAS de <c>company_id</c> (le serveur le résout du
/// principal authentifié — tenant-scoping serveur, CLAUDE.md n°9). <see cref="DeclaredOperatorIdentity"/>
/// est purement INDICATIVE et NON PROBANTE (jamais le signataire — ADR-0030 §5).
/// </summary>
internal sealed class OnSiteCapturePayload
{
    /// <summary>Crée le payload de capture.</summary>
    /// <param name="documentId">Document signé sur place.</param>
    /// <param name="signedBindingHash">SHA-256 (hex) des octets exacts de l'artefact scellé, signé par le client.</param>
    /// <param name="encryptedFssBase64">FSS chiffrée (Base64) — preuve, jamais un gabarit biométrique.</param>
    /// <param name="signatureImagePngBase64">Rendu PNG de la signature (Base64).</param>
    /// <param name="declaredOperatorIdentity">Identité opérateur déclarée (indicative, non probante), ou <c>null</c>.</param>
    /// <param name="capturedAtUtc">Horodatage de capture (UTC).</param>
    public OnSiteCapturePayload(
        Guid documentId,
        string signedBindingHash,
        string encryptedFssBase64,
        string signatureImagePngBase64,
        string? declaredOperatorIdentity,
        DateTimeOffset capturedAtUtc)
    {
        DocumentId = documentId;
        SignedBindingHash = signedBindingHash;
        EncryptedFssBase64 = encryptedFssBase64;
        SignatureImagePngBase64 = signatureImagePngBase64;
        DeclaredOperatorIdentity = declaredOperatorIdentity;
        CapturedAtUtc = capturedAtUtc;
    }

    /// <summary>Document signé sur place.</summary>
    public Guid DocumentId { get; }

    /// <summary>Empreinte de binding signée (SHA-256 hex des octets exacts de l'artefact scellé).</summary>
    public string SignedBindingHash { get; }

    /// <summary>FSS chiffrée (Base64).</summary>
    public string EncryptedFssBase64 { get; }

    /// <summary>Rendu PNG de la signature (Base64).</summary>
    public string SignatureImagePngBase64 { get; }

    /// <summary>Identité opérateur déclarée (indicative, non probante), ou <c>null</c>.</summary>
    public string? DeclaredOperatorIdentity { get; }

    /// <summary>Horodatage de capture (UTC).</summary>
    public DateTimeOffset CapturedAtUtc { get; }
}
