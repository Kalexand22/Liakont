namespace Liakont.Host.Payments;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Implémentation de <see cref="IEncaissementsConsoleQueries"/> : compose les agrégats jour×taux
/// (<see cref="IPaymentAggregationQueries"/>, PIP03a) et le paramétrage du tenant
/// (<see cref="ITenantSettingsConsoleQueries"/>) en un <see cref="EncaissementsViewModel"/>. Les deux
/// lectures sont tenant-scopées par construction (base du tenant courant — CLAUDE.md n°9/17). AUCUNE règle
/// fiscale n'est dérivée : la qualification des agrégats vient de PIP03a (reportée telle quelle) et l'« état
/// fiscal en attente » REFLÈTE cette même qualification (statut Suspended), jamais redérivée ici
/// (CLAUDE.md n°2). L'affichage adapté à la PA est piloté par sa capacité déclarée, jamais par son type
/// (CLAUDE.md n°8).
/// </summary>
internal sealed class EncaissementsConsoleQueryService : IEncaissementsConsoleQueries
{
    /// <summary>
    /// Statut « décision fiscale en attente » persisté par NOM (miroir de
    /// <c>PaymentAggregationStatus.Suspended</c>, Domain Pipeline — inaccessible depuis le Host qui ne
    /// référence que les Contracts). Identique au <c>SuspendedStatus</c> de <c>PipelineEndpointMapping</c>
    /// (<c>GET /payments</c>) : le bandeau de page reflète la MÊME qualification que les badges par ligne
    /// (calculée par PIP03a, qui suspend sur catégorie/fréquence/imputation des frais manquantes), jamais
    /// une règle redérivée ni un sous-ensemble divergent des paramètres (CLAUDE.md n°2).
    /// </summary>
    private const string SuspendedStatus = "Suspended";

    private readonly IPaymentAggregationQueries _aggregationQueries;
    private readonly ITenantSettingsConsoleQueries _settingsQueries;

    public EncaissementsConsoleQueryService(
        IPaymentAggregationQueries aggregationQueries,
        ITenantSettingsConsoleQueries settingsQueries)
    {
        _aggregationQueries = aggregationQueries;
        _settingsQueries = settingsQueries;
    }

    public async Task<EncaissementsViewModel> GetEncaissementsAsync(string? period, CancellationToken cancellationToken = default)
    {
        var aggregates = await _aggregationQueries.GetAggregationsAsync(period, cancellationToken).ConfigureAwait(false);
        var overview = await _settingsQueries.GetSettingsOverview(cancellationToken).ConfigureAwait(false);

        return new EncaissementsViewModel
        {
            Aggregates = aggregates.Select(PaymentAggregateRow.FromDto).ToList(),
            FiscalDecisionPending = aggregates.Any(a => string.Equals(a.Status, SuspendedStatus, StringComparison.Ordinal)),
            PaymentReportingSupported = SupportsPaymentReporting(overview.PaAccounts),
            HasConfiguredPa = overview.PaAccounts.Count > 0,
            PaName = ResolvePaName(overview.PaAccounts),
        };
    }

    /// <summary>
    /// <c>true</c> si au moins une PA configurée (plug-in chargé) déclare la transmission des paiements
    /// domestiques. Piloté par la capacité déclarée du plug-in, jamais par le type de PA (CLAUDE.md n°8).
    /// </summary>
    private static bool SupportsPaymentReporting(IReadOnlyList<PaAccountSettingsDto> accounts) =>
        accounts.Any(a => a.PluginAvailable && a.Capabilities?.SupportsDomesticPaymentReporting == true);

    /// <summary>
    /// Nom de la PA pour le bandeau de capacité : préfère un compte dont les capacités sont résolues (libellé
    /// opérateur de la PA), sinon le type du plug-in du premier compte ; <c>null</c> si aucune PA configurée.
    /// </summary>
    private static string? ResolvePaName(IReadOnlyList<PaAccountSettingsDto> accounts)
    {
        if (accounts.Count == 0)
        {
            return null;
        }

        var withCapabilities = accounts.FirstOrDefault(a => a.Capabilities is not null);
        return withCapabilities?.Capabilities?.PaName ?? accounts[0].Account.PluginType;
    }
}
