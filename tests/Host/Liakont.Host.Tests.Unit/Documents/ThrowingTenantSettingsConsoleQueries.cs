namespace Liakont.Host.Tests.Unit.Documents;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>Lecture du paramétrage en échec : un bouton d'envoi ne doit JAMAIS se suspendre à tort.</summary>
internal sealed class ThrowingTenantSettingsConsoleQueries : ITenantSettingsConsoleQueries
{
    public Task<TenantSettingsOverviewDto> GetSettingsOverview(CancellationToken ct = default) =>
        throw new InvalidOperationException("settings unavailable");
}
