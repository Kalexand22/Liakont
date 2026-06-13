namespace Liakont.Host.Tests.Unit.Documents;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Paramétrage du tenant pour la garde UX de suspension des envois (lot 2) : table TVA VALIDÉE par
/// défaut (envois ouverts — les tests d'envoi existants restent exerçables). Partagé par les tests
/// des deux points d'entrée d'envoi (liste Documents, fiche détail).
/// </summary>
internal sealed class FakeTenantSettingsConsoleQueries : ITenantSettingsConsoleQueries
{
    public bool TvaValidated { get; init; } = true;

    public Task<TenantSettingsOverviewDto> GetSettingsOverview(CancellationToken ct = default) =>
        Task.FromResult(new TenantSettingsOverviewDto
        {
            TvaMapping = new TvaMappingSummaryDto
            {
                MappingVersion = "v1",
                IsValidated = TvaValidated,
                DefaultBehavior = "Block",
                RuleCount = 1,
            },
            PaAccounts = [],
        });
}
