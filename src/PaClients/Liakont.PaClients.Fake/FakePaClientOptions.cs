namespace Liakont.PaClients.Fake;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Configuration du plug-in factice : c'est ce qui le rend PILOTABLE en test (acceptance PAA02 :
/// « capacités configurables par test », « réponses succès / rejet / erreur silencieuse / timeout /
/// capacité absente »). Les valeurs par défaut décrivent une PA GÉNÉREUSE (toutes les capacités V1),
/// pour que le mode démo hors-ligne fonctionne sans paramétrage ; un test restreint les capacités
/// pour prouver que le produit n'est JAMAIS bloqué par une PA limitée (résultat typé, jamais une
/// exception — PAA01).
/// </summary>
public sealed record FakePaClientOptions
{
    /// <summary>Nom par défaut du plug-in factice (clé de registre et libellé opérateur).</summary>
    public const string DefaultPaName = "Fake";

    /// <summary>
    /// Capacités déclarées de la PA factice — la seule source de vérité du comportement du produit
    /// (PAA01). Par défaut : toutes les capacités V1 (le flux international 10.2 reste à <c>false</c>,
    /// décision D2 : V1 n'alimente que le flux domestique 10.4).
    /// </summary>
    public PaCapabilities Capabilities { get; init; } = new()
    {
        PaName = DefaultPaName,
        SupportsB2cReporting = true,
        SupportsDomesticPaymentReporting = true,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = false,
        SupportsCreditNotes = true,
        SupportsTaxReportRetrieval = true,
        SupportsDocumentRetrieval = true,
        SupportsReportRectification = true,
        SupportsSelfBilling = true,
        MaxDocumentsPerRequest = null,
    };

    /// <summary>Comportement appliqué aux envois (succès par défaut).</summary>
    public FakePaScenario SendScenario { get; init; } = FakePaScenario.Success;

    /// <summary>
    /// Erreurs remontées telles quelles pour les scénarios <see cref="FakePaScenario.Rejected"/> et
    /// <see cref="FakePaScenario.SilentError"/> (F05 §3 : code + message conservés intacts). Jamais
    /// <c>null</c> ; une liste vide signifie « rejet sans détail ».
    /// </summary>
    public IReadOnlyList<PaError> RejectionErrors { get; init; } =
        [new PaError("FAKE_REJECT", "Rejet simulé par le plug-in factice.")];

    /// <summary>
    /// Tax reports de COMPTE exposés par <c>ListTaxReportsAsync</c> / <c>GetTaxReportAsync</c> (vide par défaut :
    /// le mode démo ne produit pas de ledger DGFiP). Sert à piloter le SYNC (PIP01d) : un report dont
    /// <see cref="PaTaxReport.XmlBase64"/> est renseigné est archivable en addendum.
    /// </summary>
    public IReadOnlyList<PaTaxReport> TaxReports { get; init; } = [];

    /// <summary>
    /// Identifiants de tax report rattachés à un document ÉMIS (renvoyés par <c>SendDocumentAsync</c> puis relus
    /// par <c>GetDocumentStatusAsync</c>) — vide par défaut. C'est l'attribution PAR DOCUMENT exploitée par le
    /// SYNC pour ne rattacher un ledger qu'aux documents qu'il couvre (jamais une attribution inventée).
    /// </summary>
    public IReadOnlyList<string> IssuedTaxReportIds { get; init; } = [];
}
