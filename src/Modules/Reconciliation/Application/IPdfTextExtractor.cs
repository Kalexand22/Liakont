namespace Liakont.Modules.Reconciliation.Application;

/// <summary>
/// Port d'EXTRACTION DE TEXTE d'un PDF (item TRK07, stratégie 2 — F06 §7 §1). Abstrait la bibliothèque
/// d'extraction (ADR-0010 : PdfPig, Apache-2.0, dans l'Infrastructure uniquement) pour garder le moteur
/// et l'orchestrateur PURS et testables. Pas d'OCR en V1 : un PDF scanné (image) n'a pas de texte
/// exploitable.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extrait le texte du PDF. Renvoie <c>null</c> si le PDF ne contient aucun texte exploitable (PDF
    /// scanné) ou si le contenu n'est pas un PDF lisible — JAMAIS d'exception sur un fichier malformé
    /// (un PDF illisible ne doit pas interrompre la passe de réconciliation, il devient un orphelin).
    /// </summary>
    string? TryExtractText(byte[] pdfBytes);
}
