namespace Liakont.PaClients.SuperPdp;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Capacités DÉCLARÉES de Super PDP — la seule source de vérité du comportement du produit
/// (blueprint.md §2 ; CLAUDE.md n°8/16). Valeurs PROVISOIRES codées par PAS02 = colonne « Valeur
/// provisoire (codée PAS02) » de F14 §5, qui applique déjà le principe « une capacité incertaine =
/// <c>false</c> » (ajouter-un-plugin-pa.md). Sont <c>true</c> : <see cref="PaCapabilities.SupportsB2cReporting"/>
/// (B2C ✅ vérifiée DR17 — schéma de fil à mapper) et <see cref="PaCapabilities.SupportsB2bInvoicing"/>
/// (facturation B2B ✅ vérifiée en sandbox — envoi réel facture 72272). Tout le reste est <c>false</c> tant que la sandbox (PAS03)
/// n'a rien confirmé : le produit dégrade alors en résultat TYPÉ, jamais un faux positif d'envoi
/// (CLAUDE.md n°2/3). Quand Super PDP confirmera un flux, SEULE cette déclaration changera — aucun
/// autre code produit n'est impacté (CLAUDE.md n°8) : c'est le test décisif de l'abstraction PA.
/// </summary>
internal static class SuperPdpCapabilities
{
    /// <summary>Capacités déclarées du plug-in (provisoires PAS02 — figées en sandbox par PAS03, F14 §5/§12).</summary>
    public static PaCapabilities Declared => new()
    {
        PaName = SuperPdpDefaults.PaName,

        // ✅ vérifiées en sandbox : e-reporting B2C (DR17, flux 10.3 — schéma de fil à mapper) ET
        // facturation B2B (envoi réel confirmé, facture 72272 — activée sur directive de recette, Karl 18/06/2026).
        SupportsB2cReporting = true,
        SupportsB2bInvoicing = true,

        // 🟠 points ouverts F14 §5/§12 : déclarés false tant que la sandbox/le support n'a rien confirmé
        // (CLAUDE.md n°2/3). Passer l'un à true sans vérification serait une règle inventée.
        SupportsDomesticPaymentReporting = false,      // flux 10.4 non documenté (O3).
        SupportsInternationalPaymentReporting = false, // flux 10.2 non documenté (O3).
        SupportsCreditNotes = false,                   // modèle d'avoir non confirmé (O7).
        SupportsTaxReportRetrieval = false,            // endpoints tax reports non confirmés (O2).
        SupportsDocumentRetrieval = false,             // endpoint de téléchargement non confirmé (O4).
        SupportsReportRectification = false,           // flux RE non documenté (O9).

        MaxDocumentsPerRequest = null,                 // aucune limite déclarée connue.
    };
}
