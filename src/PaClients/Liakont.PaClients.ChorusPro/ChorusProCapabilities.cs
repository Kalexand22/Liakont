namespace Liakont.PaClients.ChorusPro;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Capacités DÉCLARÉES de Chorus Pro — la seule source de vérité du comportement du produit
/// (blueprint.md §2 ; CLAUDE.md n°8/16). Chorus Pro est une PA B2G de niveau « Essentiel » : elle ne fait
/// que TRANSPORTER un Factur-X DÉJÀ scellé (dépôt <c>deposerFluxFacture</c> du flux, relecture
/// <c>consulterCR</c>) — elle ne construit aucun payload, n'émet aucun e-reporting, n'a ni cycle de vie
/// fiscal ni récupération de tax report. Chaque capacité ci-dessous est déclarée EXPLICITEMENT et justifiée :
/// rien n'est inventé, une capacité incertaine reste <c>false</c> (ajouter-un-plugin-pa.md ; CLAUDE.md n°2/3),
/// et le produit dégrade alors en résultat TYPÉ, jamais un faux positif d'envoi.
/// <para>
/// SEULE <see cref="PaCapabilities.SupportsFacturXTransmission"/> est <c>true</c> (transport du Factur-X
/// scellé livré par CP03+/CP05, niveau « Essentiel », modèle <c>Generique</c> — F18 §6/périmètre). C'est
/// cette capacité — et elle seule, jamais un <c>if (pa is ChorusPro)</c> (CLAUDE.md n°8) — qui pilote en amont
/// la génération du Factur-X à l'étape d'envoi ET fait SAUTER le diagnostic <c>tax_report_setting</c> au SEND
/// (<c>SendTenantJob</c> : une PA « Essentiel » n'a aucun réglage e-reporting, le sauter est sa seule voie de
/// transmission — F04 §3.1 ; F16 §6). L'e-reporting est EXCLU du périmètre Chorus Pro (B2G only, décision D2 —
/// F18 §8). Quand Chorus Pro confirmera un autre flux (p. ex. les avoirs), SEULE cette déclaration changera —
/// aucun autre code produit n'est impacté (CLAUDE.md n°8).
/// </para>
/// </summary>
internal static class ChorusProCapabilities
{
    /// <summary>Capacités déclarées de Chorus Pro (transport Factur-X « Essentiel » — F18, CLAUDE.md n°2/3/8).</summary>
    public static PaCapabilities Declared => new()
    {
        PaName = ChorusProDefaults.PaName,

        // ✅ SEULE capacité active : transport d'un Factur-X DÉJÀ scellé (niveau « Essentiel », F18 §6 ; patron
        // GeneriqueClient). Pilote la génération Factur-X amont au SEND et saute le diagnostic tax_report_setting
        // (une PA B2G de transport n'en a pas) — comportement VOULU et documenté (PaCapabilities.cs ; SendTenantJob).
        SupportsFacturXTransmission = true,

        // 🔒 Avoirs : false en démo. Bascule à true UNIQUEMENT sur confirmation EXPLICITE sandbox + Spec, jamais
        // déduite d'un test vert (patron SuperPdpCapabilities — CLAUDE.md n°2/3). Le modèle d'avoir Chorus Pro
        // n'est pas confirmé (F18 §6/périmètre).
        SupportsCreditNotes = false,

        // 🚫 e-reporting EXCLU du périmètre Chorus Pro (B2G only, décision D2 — F18 §8) : flux 10.3 (B2C),
        // 10.4 (domestique) et 10.2 (international) restent false → tout appel dégrade en résultat TYPÉ (PAA01).
        SupportsB2cReporting = false,
        SupportsDomesticPaymentReporting = false,
        SupportsInternationalPaymentReporting = false,

        // 🚫 Chorus Pro = B2G (administration), PAS de facturation électronique B2B (flux 1/2) — F18 §8.
        SupportsB2bInvoicing = false,

        // 🚫 Niveau « Essentiel » = transport pur : ni lecture de tax report, ni téléchargement de la facture
        // générée par la PA (Chorus Pro ne génère rien, elle transporte un artefact déjà scellé), ni
        // rectification (flux RE), ni auto-facturation sous mandat — toutes false (F18 §6, CLAUDE.md n°2).
        SupportsTaxReportRetrieval = false,
        SupportsDocumentRetrieval = false,
        SupportsReportRectification = false,
        SupportsSelfBilling = false,

        // Dépôt UNITAIRE (deposerFluxFacture, un flux par requête) : aucune limite de lot déclarée. Le dépôt
        // par lot est un fast-follow — null tant qu'aucune limite n'est confirmée (F18 §3/périmètre).
        MaxDocumentsPerRequest = null,
    };
}
