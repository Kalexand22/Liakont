namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Résultat TYPÉ d'un appel dont la capacité n'est pas prise en charge par la PA (acceptance PAA01 :
/// « capacité manquante = résultat typé, jamais d'exception, jamais de blocage du produit »). Porte
/// de quoi JOURNALISER côté module Documents un message opérateur en français (CLAUDE.md n°12) :
/// « en attente : la plateforme &lt;nom&gt; ne prend pas encore en charge &lt;capacité&gt; ».
/// </summary>
public sealed record PaCapabilityNotSupportedResult
{
    /// <summary>Nom de la PA concernée (ex. « B2Brouter »).</summary>
    public required string PaName { get; init; }

    /// <summary>Capacité absente, typée (journalisable et exploitable sans parser de texte).</summary>
    public required PaCapability Capability { get; init; }

    /// <summary>Message opérateur prêt à journaliser, en français.</summary>
    public required string OperatorMessage { get; init; }

    /// <summary>
    /// Construit un résultat « capacité absente » avec le message opérateur français standard.
    /// </summary>
    /// <param name="paName">Nom de la PA concernée.</param>
    /// <param name="capability">Capacité non prise en charge.</param>
    public static PaCapabilityNotSupportedResult Create(string paName, PaCapability capability)
    {
        var libelle = FrenchLabel(capability);
        return new PaCapabilityNotSupportedResult
        {
            PaName = paName,
            Capability = capability,
            OperatorMessage =
                $"En attente : la plateforme « {paName} » ne prend pas encore en charge {libelle}.",
        };
    }

    private static string FrenchLabel(PaCapability capability) => capability switch
    {
        PaCapability.B2cReporting => "l'e-reporting B2C (flux 10.3)",
        PaCapability.DomesticPaymentReporting => "l'e-reporting de paiement domestique (flux 10.4)",
        PaCapability.InternationalPaymentReporting => "l'e-reporting de paiement international (flux 10.2)",
        PaCapability.B2bInvoicing => "la facturation électronique B2B (flux 1/2)",
        PaCapability.CreditNotes => "les avoirs",
        PaCapability.TaxReportRetrieval => "la récupération des tax reports",
        PaCapability.DocumentRetrieval => "le téléchargement de la facture générée",
        PaCapability.ReportRectification => "la rectification de déclaration (flux RE)",
        PaCapability.SelfBilling => "l'émission d'auto-factures sous mandat (389)",
        _ => capability.ToString(),
    };
}
