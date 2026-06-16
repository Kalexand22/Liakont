namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Capacité PA nommée — permet à un résultat « capacité absente » d'être TYPÉ et journalisable
/// (acceptance PAA01 : « capacité non supportée → CapabilityNotSupported journalisable »). Chaque
/// valeur correspond à un drapeau de <see cref="PaCapabilities"/>.
/// </summary>
public enum PaCapability
{
    /// <summary>E-reporting B2C — flux 10.3.</summary>
    B2cReporting = 1,

    /// <summary>E-reporting de paiement domestique — flux 10.4.</summary>
    DomesticPaymentReporting = 2,

    /// <summary>E-reporting de paiement international — flux 10.2.</summary>
    InternationalPaymentReporting = 3,

    /// <summary>Facturation électronique B2B — flux 1/2.</summary>
    B2bInvoicing = 4,

    /// <summary>Émission d'avoirs.</summary>
    CreditNotes = 5,

    /// <summary>Récupération des tax reports.</summary>
    TaxReportRetrieval = 6,

    /// <summary>Téléchargement de la facture générée par la PA.</summary>
    DocumentRetrieval = 7,

    /// <summary>Rectification de déclaration — flux RE.</summary>
    ReportRectification = 8,

    /// <summary>Émission d'auto-factures sous mandat (type BT-3 = 389, art. 289 I-2 CGI — F15 §1.2).</summary>
    SelfBilling = 9,
}
