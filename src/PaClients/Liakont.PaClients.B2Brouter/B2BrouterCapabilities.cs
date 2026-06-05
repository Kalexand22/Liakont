namespace Liakont.PaClients.B2Brouter;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Capacités DÉCLARÉES de B2Brouter — la seule source de vérité du comportement du produit
/// (blueprint.md §2 ; CLAUDE.md n°8/16). Chaque drapeau reflète ce que CE BUILD du plug-in supporte
/// RÉELLEMENT (invariant de cohérence « déclaré = comportement », vérifié par la suite de contrat) :
/// PAB01 livre l'ENVOI (B2C + avoirs) ; la récupération de tax reports / facture générée et le
/// reporting de paiement sont activés au fil des items suivants. PAB03 FINALISE cette déclaration
/// (état réel au 2026-06, avec références aux points ouverts F09 / vérifications staging).
/// </summary>
internal static class B2BrouterCapabilities
{
    /// <summary>Capacités déclarées du plug-in, FINALISÉES par PAB03 (état réel B2Brouter au 2026-06).</summary>
    public static PaCapabilities Declared => new()
    {
        PaName = B2BrouterDefaults.PaName,

        // Livrés par PAB01 (SendDocumentAsync) :
        SupportsB2cReporting = true,        // e-reporting B2C (flux 10.3) — envoi validé staging (F05).
        SupportsCreditNotes = true,         // avoirs via is_credit_note + amended_* (F05 ; F07-F08).

        // Livré par PAB03 (List/Get tax reports + réglage idempotent) :
        SupportsTaxReportRetrieval = true,  // PAB03 §1-§3 (lectures + EnsureTaxReportSetting).

        // Capacités dont l'endpoint/flux n'est PAS confirmé en staging (déclaration honnête = false tant
        // que ce n'est pas vérifié — CLAUDE.md n°2/3 ; vérification portée par la suite staging PAB04) :
        SupportsDocumentRetrieval = false,   // PAB03 §4 : endpoint de téléchargement de la facture générée non confirmé (ticket support).
        SupportsReportRectification = false, // flux RE — à vérifier en staging (PIP04 / PAB03 §5).

        // Capacités réellement absentes de B2Brouter (état 2026-06, PAB03 §5) :
        SupportsDomesticPaymentReporting = false,      // Flux 10.4 « planned for a future release » (F09).
        SupportsInternationalPaymentReporting = false, // Flux 10.2 — idem.
        SupportsB2bInvoicing = false,                  // phase 2.

        MaxDocumentsPerRequest = null,
    };
}
