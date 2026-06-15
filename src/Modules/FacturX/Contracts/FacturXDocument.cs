namespace Liakont.Modules.FacturX.Contracts;

/// <summary>
/// Artefact Factur-X produit par la plateforme (ADR-0023) : un PDF/A-3 scellé portant le
/// <c>factur-x.xml</c> (XML CII EN 16931) embarqué. Surface publique du module : le pipeline (FX07)
/// passe cet artefact PRÉ-CONSTRUIT au plug-in PA générique (FXG) via le contrat <c>IPaClient</c>
/// étendu ; le plug-in ne le régénère JAMAIS (artefact absent → blocage). DTO pur (octets +
/// métadonnées), aucune logique, aucune règle fiscale (CLAUDE.md n°2).
/// </summary>
public sealed class FacturXDocument
{
    /// <summary>Crée un artefact Factur-X.</summary>
    /// <param name="pdfBytes">Le PDF/A-3 scellé : rendu visuel lisible + <c>factur-x.xml</c> embarqué.</param>
    /// <param name="fileName">Nom de fichier proposé pour le PDF (p. ex. <c>FAC-2026-0001.pdf</c>).</param>
    /// <param name="crossIndustryInvoiceXml">Le XML CII EN 16931 embarqué dans le PDF/A-3 (<c>factur-x.xml</c>).</param>
    public FacturXDocument(byte[] pdfBytes, string fileName, byte[] crossIndustryInvoiceXml)
    {
        PdfBytes = pdfBytes;
        FileName = fileName;
        CrossIndustryInvoiceXml = crossIndustryInvoiceXml;
    }

    /// <summary>Le PDF/A-3 scellé (rendu visuel + <c>factur-x.xml</c> embarqué).</summary>
    public byte[] PdfBytes { get; }

    /// <summary>Nom de fichier proposé pour le PDF.</summary>
    public string FileName { get; }

    /// <summary>Le XML CII EN 16931 embarqué (<c>factur-x.xml</c>).</summary>
    public byte[] CrossIndustryInvoiceXml { get; }
}
