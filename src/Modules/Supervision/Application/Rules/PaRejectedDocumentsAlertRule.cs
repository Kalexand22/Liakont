namespace Liakont.Modules.Supervision.Application.Rules;

using System.Globalization;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Règle « Rejets PA non traités » (F12 §5.2) : 🔴 Critique si le document le plus ancien en état
/// <c>RejectedByPa</c> n'a pas été retraité depuis plus que le seuil (défaut 2 jours, surchargeable par
/// tenant — CFG02). Un rejet de la Plateforme Agréée laissé sans suite = obligation déclarative non honorée :
/// gravité critique, seuil plus serré que les documents bloqués.
/// </summary>
public sealed class PaRejectedDocumentsAlertRule : DocumentStateAgeAlertRule
{
    /// <summary>Seuil par défaut produit (F12 §5.2) ; surchargé par <c>AlertThresholdsDto.PaRejectionsDays</c>.</summary>
    private const int DefaultPaRejectionsDays = 2;

    public PaRejectedDocumentsAlertRule(IDocumentQueries documents, ITenantSettingsQueries tenantSettings)
        : base(documents, tenantSettings)
    {
    }

    public override string RuleKey => "documents.pa_rejected";

    public override AlertSeverity Severity => AlertSeverity.Critical;

    // "RejectedByPa" = nom persisté de DocumentState.RejectedByPa (F06 §3) ; cf. BlockedDocumentsAlertRule.
    protected override string State => "RejectedByPa";

    protected override int DefaultThresholdDays => DefaultPaRejectionsDays;

    protected override int TenantThresholdDays(AlertThresholdsDto thresholds) => thresholds.PaRejectionsDays;

    protected override string BuildDetail(DocumentSummaryDto oldest, int thresholdDays) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Le document {0}, rejeté par la Plateforme Agréée le {1} (seuil {2} j), n'a pas été retraité. Corrigez et renvoyez le document, ou traitez-le manuellement.",
            oldest.DocumentNumber,
            FormatUtc(oldest.LastUpdateUtc),
            thresholdDays);
}
