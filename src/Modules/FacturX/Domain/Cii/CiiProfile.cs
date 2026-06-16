namespace Liakont.Modules.FacturX.Domain.Cii;

/// <summary>
/// Constantes <b>structurelles et de profil</b> du Cross Industry Invoice (CII, UN/CEFACT D22B) pour le
/// profil EN 16931 (COMFORT). Non fiscales au sens d'INV-FX-2 : ce sont des espaces de noms XML, un
/// identifiant de spécification (BT-24), un code de type de document (BT-3 facture), un code de type de
/// taxe (« VAT »), un schéma d'identifiant légal et une unité de mesure neutre — <b>aucune</b> catégorie
/// de TVA, taux ou code VATEX (ces valeurs qualitatives ne viennent QUE du pivot, mapping F03).
/// <para>
/// Valeurs SOURCÉES (jamais inventées, CLAUDE.md n°2) : les espaces de noms et l'ordre des éléments sont
/// ceux du XSD CII D22B fourni par la DGFiP v3.2 (<c>docs/references/dgfip-v3.2/3- XSD_v3.2/2 -
/// E-invoicing/F1_FULL_CII_D22B</c>) ; <see cref="SpecificationIdentifier"/> (BT-24), <see cref="VatTypeCode"/>,
/// <see cref="InvoiceTypeCode"/> (BT-3), <see cref="SirenScheme"/> et <see cref="DefaultUnitCode"/> sont
/// repris à l'identique des constantes confirmées en sandbox EN 16931 (<c>SuperPdpDefaults</c>, F14 §3.2).
/// </para>
/// </summary>
public static class CiiProfile
{
    /// <summary>Espace de noms racine <c>rsm:</c> (CrossIndustryInvoice, UN/CEFACT D22B, version 100).</summary>
    public const string RsmNamespace = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";

    /// <summary>Espace de noms <c>ram:</c> (Reusable Aggregate Business Information Entity).</summary>
    public const string RamNamespace =
        "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";

    /// <summary>Espace de noms <c>udt:</c> (Unqualified Data Type).</summary>
    public const string UdtNamespace = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";

    /// <summary>Espace de noms <c>qdt:</c> (Qualified Data Type).</summary>
    public const string QdtNamespace = "urn:un:unece:uncefact:data:standard:QualifiedDataType:100";

    /// <summary>
    /// Identifiant de spécification EN 16931 (BT-24), porté par
    /// <c>GuidelineSpecifiedDocumentContextParameter/ID</c> — valeur normative confirmée
    /// (<c>SuperPdpDefaults.SpecificationIdentifier</c>, F14 §3.2). L'identité « profil Factur-X EN 16931 »
    /// est portée séparément par le XMP <c>fx:ConformanceLevel = EN 16931</c> (FX04, INV-FX-3).
    /// </summary>
    public const string SpecificationIdentifier = "urn:cen.eu:en16931:2017";

    /// <summary>
    /// Code de type de document UNTDID 1001 d'une facture commerciale (BT-3) : <c>380</c>. Les avoirs
    /// (<c>381</c>) ne sont pas couverts par le sérialiseur V1 — la classification 380/381 vit dans le
    /// module Validation (mapping pivot, ADR-0004 D3-3) ; un avoir relève d'un lot ultérieur, pas d'une
    /// déduction du sérialiseur. Aligné sur <c>SuperPdpDefaults.CommercialInvoiceTypeCode</c>.
    /// </summary>
    public const string InvoiceTypeCode = "380";

    /// <summary>Code de type de taxe UNTDID 5153 « TVA » (<c>VAT</c>) porté par chaque <c>ApplicableTradeTax</c>.</summary>
    public const string VatTypeCode = "VAT";

    /// <summary>
    /// Schéma ISO 6523 du SIREN (<c>0002</c>), porté par <c>SpecifiedLegalOrganization/ID@schemeID</c>
    /// (BT-30). Aligné sur <c>SuperPdpDefaults.SirenScheme</c>.
    /// </summary>
    public const string SirenScheme = "0002";

    /// <summary>Schéma de l'identifiant de TVA (<c>VA</c>) porté par <c>SpecifiedTaxRegistration/ID@schemeID</c> (BT-31).</summary>
    public const string VatScheme = "VA";

    /// <summary>
    /// Unité de mesure neutre UN/ECE Rec 20 « one » (<c>C62</c>, BT-130) — utilisée quand le pivot ne
    /// porte pas d'unité. Aligné sur <c>SuperPdpDefaults.DefaultQuantityUnitCode</c> (unité, non fiscale).
    /// </summary>
    public const string DefaultUnitCode = "C62";

    /// <summary>Format de date CII (<c>udt:DateTimeString@format</c> / <c>udt:DateString@format</c>) : <c>102</c> = CCYYMMDD.</summary>
    public const string DateFormatCode = "102";
}
