namespace Liakont.Modules.FacturX.Domain;

/// <summary>
/// Constantes de PROFIL Factur-X (scellement PDF/A-3 + XMP), <b>non fiscales</b>, recopiées VERBATIM
/// d'ADR-0023 §3 / INV-FX-3. Centralise les identifiants de scellement consommés par le scellement
/// PDF/A-3 (FX04). AUCUNE valeur fiscale qualitative (catégorie TVA, taux, VATEX) n'apparaît ici :
/// celles-ci sont RECOPIÉES du pivot par le sérialiseur CII (FX03), jamais constantes (ADR-0023
/// INV-FX-2 ; CLAUDE.md n°2). <c>fx:Version</c> n'est volontairement PAS figée ici : ADR-0023 §3 et
/// les critères de sortie V1 la figent au moment de coder le scellement (FX04).
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
