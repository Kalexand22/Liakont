namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

/// <summary>Double d'<see cref="IIntegrationConfigQueries"/> renvoyant des jeux fixés (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeIntegrationConfigQueries : IIntegrationConfigQueries
{
    private readonly IReadOnlyList<IntegrationConfigDto> _configs;

    public FakeIntegrationConfigQueries(IReadOnlyList<IntegrationConfigDto>? configs = null) => _configs = configs ?? [];

    public Task<IntegrationConfigDto?> GetByType(string integrationType, Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(_configs.FirstOrDefault(c => c.IntegrationType == integrationType));

    public Task<IReadOnlyList<IntegrationConfigDto>> ListByCompany(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(_configs);
}
