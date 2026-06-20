namespace Liakont.PaClients.ChorusPro;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Capacités DÉCLARÉES de Chorus Pro — la seule source de vérité du comportement du produit
/// (blueprint.md §2 ; CLAUDE.md n°8/16). SQUELETTE CP02 : TOUT est <c>false</c> tant que le transport
/// (dépôt <c>deposerFluxFacture</c>, relecture <c>consulterCR</c>…) n'est pas livré par CP03+ — une
/// capacité incertaine = <c>false</c> (ajouter-un-plugin-pa.md ; CLAUDE.md n°2/3). Le produit dégrade
/// alors en résultat TYPÉ, jamais un faux positif d'envoi.
/// <para>
/// Quand CP03 livrera le dépôt, SEULE <see cref="PaCapabilities.SupportsFacturXTransmission"/> passera à
/// <c>true</c> (Chorus Pro = transport pur d'un Factur-X DÉJÀ scellé, niveau « Essentiel », modèle
/// <c>Generique</c> — F18 §6/périmètre) : aucun autre code produit n'est impacté (CLAUDE.md n°8).
/// L'e-reporting est EXCLU du périmètre (B2G only, décision D2 — F18 §8).
/// </para>
/// </summary>
internal static class ChorusProCapabilities
{
    /// <summary>Capacités déclarées du squelette (toutes false — F18, CLAUDE.md n°2/3).</summary>
    public static PaCapabilities Declared => new()
    {
        PaName = ChorusProDefaults.PaName,

        // SQUELETTE CP02 : rien n'est encore implémenté → toutes false (CLAUDE.md n°2/3). CP03 activera
        // SupportsFacturXTransmission (transport du Factur-X scellé) ; l'e-reporting reste EXCLU (F18 §8).
        SupportsFacturXTransmission = false,
        SupportsB2cReporting = false,
        SupportsB2bInvoicing = false,
        SupportsDomesticPaymentReporting = false,
        SupportsInternationalPaymentReporting = false,
        SupportsCreditNotes = false,
        SupportsTaxReportRetrieval = false,
        SupportsDocumentRetrieval = false,
        SupportsReportRectification = false,
        SupportsSelfBilling = false,

        MaxDocumentsPerRequest = null,
    };
}
