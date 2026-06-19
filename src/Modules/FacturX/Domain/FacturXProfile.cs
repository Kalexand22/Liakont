namespace Liakont.Modules.FacturX.Domain;

/// <summary>
/// Constantes de PROFIL Factur-X (scellement PDF/A-3 + XMP), <b>non fiscales</b>, recopiées VERBATIM
/// d'ADR-0023 §3 / INV-FX-3. Centralise les identifiants de scellement consommés par le scellement
/// PDF/A-3 (FX04). AUCUNE valeur fiscale qualitative (catégorie TVA, taux, VATEX) n'apparaît ici :
/// celles-ci sont RECOPIÉES du pivot par le sérialiseur CII (FX03), jamais constantes (ADR-0023
/// INV-FX-2 ; CLAUDE.md n°2). <c>fx:Version</c> est FIGÉE ici par FX04 (scellement) : ADR-0023 §3 et
/// les critères de sortie V1 imposent de la figer au moment de coder, jamais de l'inventer.
/// </summary>
public static class FacturXProfile
{
    /// <summary>
    /// URI d'extension XMP Factur-X — la casse et le « # » final sont OBLIGATOIRES (ADR-0023 §3,
    /// INV-FX-3).
    /// </summary>
    public const string XmpExtensionUri = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";

    /// <summary>
    /// Niveau de conformité XMP <c>fx:ConformanceLevel</c> — AVEC l'espace (ADR-0023 §3, INV-FX-3).
    /// </summary>
    public const string XmpConformanceLevel = "EN 16931";

    /// <summary>
    /// Version MAJEURE du standard Factur-X portée par le XMP <c>fx:Version</c> (ADR-0023 §3 + critères
    /// de sortie V1). FIGÉE à <c>1.0</c> au moment de coder FX04 — c'est la version majeure du standard
    /// (FNFE-MPE), à NE PAS confondre avec la version de spécification (1.0.05 / DGFiP v3.2). Jamais
    /// inventée : valeur conventionnelle confirmée (exemple ZUGFeRD officiel QuestPDF).
    /// </summary>
    public const string XmpVersion = "1.0";

    /// <summary>Type de document XMP <c>fx:DocumentType</c> (ADR-0023 §3).</summary>
    public const string XmpDocumentType = "INVOICE";

    /// <summary>
    /// Nom de fichier de la pièce jointe CII embarquée — <c>fx:DocumentFileName</c> (ADR-0023 §3).
    /// </summary>
    public const string AttachmentFileName = "factur-x.xml";

    /// <summary>
    /// Relation <c>/AFRelationship</c> de la pièce jointe PDF/A-3 — valeur la plus interopérable
    /// retenue par prudence (ADR-0023 §3, INV-FX-3).
    /// </summary>
    public const string AttachmentRelationship = "Alternative";
}
