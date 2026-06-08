namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Projection de présentation des capacités déclarées d'une Plateforme Agréée (<c>PaCapabilities</c> du
/// module Transmission), exposée par <c>GET /api/v1/settings</c> (API01c). C'est une PROJECTION LOCALE
/// (pas le type Transmission) : le module TenantSettings ne référence pas Transmission.Contracts dans sa
/// surface publique (frontière Contracts ; les Contracts d'un module ne dépendent que de Common). La
/// composition lit les capacités via <c>IPaClientRegistry</c> dans l'Infrastructure et les recopie ici.
/// La console s'en sert pour afficher/masquer des fonctionnalités selon ce que la PA déclare — jamais un
/// <c>if (pa is …)</c> ni un flag produit (CLAUDE.md n°8/16).
/// </summary>
public record PaCapabilitiesSummaryDto
{
    /// <summary>Nom de la PA (libellé opérateur, ex. « B2Brouter »).</summary>
    public required string PaName { get; init; }

    /// <summary>E-reporting B2C (flux 10.3).</summary>
    public required bool SupportsB2cReporting { get; init; }

    /// <summary>E-reporting de paiement domestique (flux 10.4).</summary>
    public required bool SupportsDomesticPaymentReporting { get; init; }

    /// <summary>E-reporting de paiement international (flux 10.2).</summary>
    public required bool SupportsInternationalPaymentReporting { get; init; }

    /// <summary>Facturation électronique B2B (flux 1/2, phase 2).</summary>
    public required bool SupportsB2bInvoicing { get; init; }

    /// <summary>Émission d'avoirs.</summary>
    public required bool SupportsCreditNotes { get; init; }

    /// <summary>Récupération des tax reports (lecture).</summary>
    public required bool SupportsTaxReportRetrieval { get; init; }

    /// <summary>Téléchargement de la facture électronique générée par la PA (Factur-X/UBL/CII).</summary>
    public required bool SupportsDocumentRetrieval { get; init; }

    /// <summary>Rectification de déclaration (flux RE).</summary>
    public required bool SupportsReportRectification { get; init; }

    /// <summary>Nombre maximal de documents par requête, ou <c>null</c> si la PA ne déclare pas de limite.</summary>
    public int? MaxDocumentsPerRequest { get; init; }
}
