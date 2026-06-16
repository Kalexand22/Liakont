namespace Liakont.PaClients.Generique;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Capacités déclarées du plug-in générique (F16 §6) : UNIQUEMENT
/// <see cref="PaCapabilities.SupportsFacturXTransmission"/> = vrai. C'est une PA de niveau « Essentiel »
/// — elle ne fait que TRANSPORTER un Factur-X déjà scellé (email / dépôt de fichier) ; elle ne construit
/// pas de payload, n'émet pas d'e-reporting, ne récupère ni statut ni tax report, n'a pas de cycle de vie.
/// Toute autre capacité absente dégrade en résultat TYPÉ, jamais une exception (acceptance PAA01).
/// </summary>
internal static class GeneriqueCapabilities
{
    /// <summary>Les capacités du plug-in générique — la seule source de vérité de son comportement.</summary>
    public static readonly PaCapabilities Value = new()
    {
        PaName = GeneriqueDefaults.PaName,
        SupportsFacturXTransmission = true,

        // Tout le reste = false (niveau « Essentiel ») : aucune émission de payload, aucun e-reporting,
        // aucune récupération, aucune rectification. Laissé explicite pour la lisibilité de la frontière.
        SupportsB2cReporting = false,
        SupportsDomesticPaymentReporting = false,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = false,
        SupportsCreditNotes = false,
        SupportsTaxReportRetrieval = false,
        SupportsDocumentRetrieval = false,
        SupportsReportRectification = false,
    };
}
