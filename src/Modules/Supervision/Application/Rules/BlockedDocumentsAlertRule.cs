namespace Liakont.Modules.Supervision.Application.Rules;

using System.Globalization;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Règle « Documents bloqués non traités » (F12 §5.2) : 🟠 Avertissement si le document le plus ancien en
/// état <c>Blocked</c> y stagne depuis plus que le seuil (défaut 5 jours, surchargeable par tenant — CFG02).
/// Un document bloqué attend une correction opérateur (ex. table TVA non validée) ; au-delà du seuil, le
/// risque de retard déclaratif justifie de le signaler.
/// </summary>
public sealed class BlockedDocumentsAlertRule : DocumentStateAgeAlertRule
{
    /// <summary>Seuil par défaut produit (F12 §5.2) ; surchargé par <c>AlertThresholdsDto.BlockedDocumentsDays</c>.</summary>
    private const int DefaultBlockedDocumentsDays = 5;

    public BlockedDocumentsAlertRule(IDocumentQueries documents, ITenantSettingsQueries tenantSettings)
        : base(documents, tenantSettings)
    {
    }

    public override string RuleKey => "documents.blocked";

    public override AlertSeverity Severity => AlertSeverity.Warning;

    // "Blocked" = nom persisté de DocumentState.Blocked (F06 §3) ; le module Supervision ne référence que les
    // Contracts de Documents (module-rules §3), pas son enum de Domain — la valeur texte est le contrat stable.
    protected override string State => "Blocked";

    protected override int DefaultThresholdDays => DefaultBlockedDocumentsDays;

    protected override int TenantThresholdDays(AlertThresholdsDto thresholds) => thresholds.BlockedDocumentsDays;

    // Message stable même si le document nommé est traité alors qu'un autre maintient l'alerte (le moteur ne
    // rafraîchit pas le Detail) : « au moins un … le plus ancien » + renvoi console (CLAUDE.md n°12).
    protected override string BuildDetail(DocumentSummaryDto oldest, int thresholdDays) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Au moins un document est bloqué depuis plus de {2} j sans traitement (le plus ancien : {0}, depuis le {1}). Corrigez les données ou le paramétrage en cause (ex. table TVA) pour qu'ils puissent être transmis ; la console liste les documents bloqués à jour.",
            oldest.DocumentNumber,
            FormatUtc(oldest.LastUpdateUtc),
            thresholdDays);
}
