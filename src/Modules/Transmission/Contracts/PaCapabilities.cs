namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Capacités déclarées d'une PA — le CŒUR de l'indépendance produit (blueprint.md §2 ; F05). Le
/// produit s'adapte aux capacités déclarées : il ne désactive jamais une fonctionnalité « parce
/// qu'une PA ne le supporte pas » par un flag produit ni un <c>if (pa is …)</c> (CLAUDE.md n°8/16).
/// Les flux 10.4 (domestique) et 10.2 (international) sont DEUX capacités séparées (F01-F02 §1 : le
/// routage et les schémas DGFiP diffèrent — acceptance PAA01).
/// </summary>
public sealed record PaCapabilities
{
    /// <summary>Nom de la PA (ex. « B2Brouter ») — porté dans les messages opérateur (français).</summary>
    public required string PaName { get; init; }

    /// <summary>E-reporting B2C — flux 10.3.</summary>
    public bool SupportsB2cReporting { get; init; }

    /// <summary>E-reporting de paiement domestique — flux 10.4 (paiements B2C).</summary>
    public bool SupportsDomesticPaymentReporting { get; init; }

    /// <summary>E-reporting de paiement international — flux 10.2.</summary>
    public bool SupportsInternationalPaymentReporting { get; init; }

    /// <summary>
    /// Facturation électronique B2B (flux 1/2) : la PA délivre la facture structurée EN 16931 au
    /// destinataire en tant que PDP. La PRODUCTION de la facture relève du produit (Lot 1 — la plateforme
    /// génère le Factur-X EN 16931, lignes BG-25 incluses) ; cette capacité reflète seulement si CETTE PA
    /// sait la router en B2B. Le transport/routage Flux 1/2 (annuaire, PPF, cycle de vie) relève du PDP,
    /// jamais du produit.
    /// </summary>
    public bool SupportsB2bInvoicing { get; init; }

    /// <summary>Émission d'avoirs.</summary>
    public bool SupportsCreditNotes { get; init; }

    /// <summary>Récupération des tax reports (lecture).</summary>
    public bool SupportsTaxReportRetrieval { get; init; }

    /// <summary>Téléchargement de la facture électronique générée par la PA (Factur-X/UBL/CII) → archivage TRK05.</summary>
    public bool SupportsDocumentRetrieval { get; init; }

    /// <summary>Rectification de déclaration — flux RE (PIP04).</summary>
    public bool SupportsReportRectification { get; init; }

    /// <summary>
    /// Émission d'auto-factures sous mandat (type BT-3 = 389, art. 289 I-2 CGI — F15 §1.2/§1.8, MND07).
    /// Pilote la projection 389 du payload sortant : une PA qui ne déclare PAS cette capacité ne reçoit
    /// JAMAIS un document self-billed (il est bloqué en amont, jamais dégradé en facture standard —
    /// CLAUDE.md n°3/8/16). Jamais un <c>if (pa is …)</c> ni un flag produit.
    /// </summary>
    public bool SupportsSelfBilling { get; init; }

    /// <summary>
    /// Transmission d'un Factur-X DÉJÀ SCELLÉ produit par la plateforme (niveau « Essentiel », F16 §6) :
    /// la PA ne fait que TRANSPORTER l'artefact (email / dépôt de fichier), elle ne le construit pas et
    /// n'a ni statut ni cycle de vie. C'est cette capacité — déclarée par la seule PA générique — qui
    /// pilote la génération du Factur-X à l'étape d'envoi (jamais un <c>if (pa is Generique)</c>,
    /// CLAUDE.md n°8). Les PA de niveau « Pilotage » (Super PDP, B2Brouter) la déclarent <c>false</c>.
    /// </summary>
    public bool SupportsFacturXTransmission { get; init; }

    /// <summary>Nombre maximal de documents par requête, ou <c>null</c> si la PA ne déclare pas de limite.</summary>
    public int? MaxDocumentsPerRequest { get; init; }

    /// <summary>
    /// Vrai si la capacité requise pour le flux de paiement demandé est déclarée. Centralise le test
    /// pour que ni le produit ni un plug-in n'aient à <c>if</c> sur le type de PA (CLAUDE.md n°16).
    /// </summary>
    /// <param name="flux">Type de flux de paiement (domestique / international).</param>
    public bool SupportsPaymentReport(PaymentReportFlux flux) => flux switch
    {
        PaymentReportFlux.Domestic => SupportsDomesticPaymentReporting,
        PaymentReportFlux.International => SupportsInternationalPaymentReporting,
        _ => false,
    };
}
