namespace Liakont.Modules.Supervision.Application.Rules;

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Règle « Agent muet » (F12 §5.2) : 🔴 Critique si un agent NON révoqué du tenant n'a émis AUCUN heartbeat
/// depuis plus que le seuil (défaut 24 h, surchargeable par tenant — CFG02). La source de données est le
/// registre des agents (<see cref="IAgentQueries.ListByTenantAsync"/> → <c>LastSeenAtUtc</c>, télémétrie
/// DÉJÀ persistée à chaque heartbeat, PIV05) ; aucune donnée fabriquée. Un agent JAMAIS vu compte son
/// silence depuis son enregistrement (<c>CreatedAt</c>) — une installation qui n'a jamais appelé est
/// exactement la panne silencieuse à détecter. Un agent révoqué est exclu (il ne parle plus par conception,
/// ce n'est pas une anomalie). La règle est PURE : le moteur SUP01a porte l'anti-bruit / l'auto-résolution.
/// </summary>
public sealed class AgentMuteAlertRule : IAlertRule
{
    /// <summary>Seuil par défaut produit (F12 §5.2) ; surchargé par <c>AlertThresholdsDto.AgentSilentHours</c>
    /// (TenantSettings/CFG02). Dupliqué ici car la valeur du Domain TenantSettings n'est pas accessible
    /// hors de ce module (module-rules §3) — la source reste F12 §5.2.</summary>
    private const int DefaultAgentSilentHours = 24;

    private readonly IAgentQueries _agents;
    private readonly ITenantSettingsQueries _tenantSettings;

    public AgentMuteAlertRule(IAgentQueries agents, ITenantSettingsQueries tenantSettings)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(tenantSettings);

        _agents = agents;
        _tenantSettings = tenantSettings;
    }

    public string RuleKey => "agent.mute";

    public AlertSeverity Severity => AlertSeverity.Critical;

    public async Task<AlertEvaluation> EvaluateAsync(AlertEvaluationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var agents = await _agents.ListByTenantAsync(context.TenantId, cancellationToken).ConfigureAwait(false);

        // Un agent révoqué ne communique plus par conception : on ne le surveille pas (sinon faux positif perpétuel).
        var active = agents.Where(static a => !a.IsRevoked).ToList();
        if (active.Count == 0)
        {
            return AlertEvaluation.Clear();
        }

        var silentHours = await ResolveSilentHoursAsync(cancellationToken).ConfigureAwait(false);
        var limit = TimeSpan.FromHours(silentHours);

        // Un agent jamais vu (LastSeenAtUtc nul) mesure son silence depuis son enregistrement (CreatedAt).
        // On retient l'agent le PLUS silencieux (référence la plus ancienne) pour le message opérateur.
        AgentSummaryDto? mostSilent = null;
        var mostSilentReference = DateTimeOffset.MaxValue;

        foreach (var agent in active)
        {
            var reference = agent.LastSeenAtUtc ?? agent.CreatedAt;
            if (context.NowUtc - reference <= limit)
            {
                continue;
            }

            if (reference < mostSilentReference)
            {
                mostSilent = agent;
                mostSilentReference = reference;
            }
        }

        if (mostSilent is null)
        {
            return AlertEvaluation.Clear();
        }

        // Le moteur ne rafraîchit pas le Detail tant que l'alerte reste active : on formule un message qui
        // reste juste si l'agent nommé est résolu mais qu'un autre maintient la condition (« au moins un … le
        // plus ancien ») et on renvoie vers la console pour la liste à jour (CLAUDE.md n°12, message actionnable).
        var detail = mostSilent.LastSeenAtUtc is null
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Au moins un agent n'a émis aucun heartbeat depuis plus de {2} h (le plus ancien constaté : « {0} », enregistré le {1}). Vérifiez l'installation et le démarrage du service Liakont Agent sur les postes concernés ; la console liste les agents muets à jour.",
                mostSilent.Name,
                FormatUtc(mostSilentReference),
                silentHours)
            : string.Format(
                CultureInfo.InvariantCulture,
                "Au moins un agent ne répond plus depuis plus de {2} h (le plus ancien constaté : « {0} », dernier contact le {1}). Vérifiez que les serveurs concernés sont allumés et que le service Liakont Agent y est démarré ; la console liste les agents muets à jour.",
                mostSilent.Name,
                FormatUtc(mostSilentReference),
                silentHours);

        return AlertEvaluation.Firing(detail);
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private async Task<int> ResolveSilentHoursAsync(CancellationToken cancellationToken)
    {
        var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
        if (companyId is not { } id)
        {
            return DefaultAgentSilentHours;
        }

        var thresholds = await _tenantSettings.GetAlertThresholds(id, cancellationToken).ConfigureAwait(false);
        return thresholds?.AgentSilentHours ?? DefaultAgentSilentHours;
    }
}
