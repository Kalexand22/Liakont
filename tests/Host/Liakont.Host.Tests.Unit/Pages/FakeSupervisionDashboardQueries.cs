namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>
/// Faux <see cref="ISupervisionDashboardQueries"/> pour les tests bUnit des pages de supervision (SUP02) :
/// renvoie une vue d'ensemble ou un détail fixés, simule un échec de lecture, et ENREGISTRE les
/// acquittements pour vérifier le câblage de la quick-action sans toucher de base.
/// </summary>
internal sealed class FakeSupervisionDashboardQueries : ISupervisionDashboardQueries
{
    private readonly IReadOnlyList<TenantSupervisionRowDto>? _overview;
    private readonly TenantSupervisionDetailDto? _detail;
    private readonly bool _throws;
    private bool _ackResult = true;

    private FakeSupervisionDashboardQueries(
        IReadOnlyList<TenantSupervisionRowDto>? overview,
        TenantSupervisionDetailDto? detail,
        bool throws)
    {
        _overview = overview;
        _detail = detail;
        _throws = throws;
    }

    /// <summary>Acquittements reçus (tenant, alerte, opérateur), dans l'ordre d'appel.</summary>
    public List<(string TenantId, Guid AlertId, string Operator)> Acknowledgements { get; } = [];

    public static FakeSupervisionDashboardQueries WithOverview(params TenantSupervisionRowDto[] rows) =>
        new(rows, detail: null, throws: false);

    public static FakeSupervisionDashboardQueries WithDetail(TenantSupervisionDetailDto? detail) =>
        new(overview: null, detail, throws: false);

    public static FakeSupervisionDashboardQueries Throwing() =>
        new(overview: null, detail: null, throws: true);

    public static FakeSupervisionDashboardQueries WithDetailAckFailing(TenantSupervisionDetailDto? detail)
    {
        var fake = new FakeSupervisionDashboardQueries(overview: null, detail, throws: false);
        fake._ackResult = false;
        return fake;
    }

    public Task<IReadOnlyList<TenantSupervisionRowDto>> GetInstanceOverviewAsync(CancellationToken cancellationToken = default)
    {
        if (_throws)
        {
            throw new InvalidOperationException("Échec simulé de la supervision.");
        }

        return Task.FromResult(_overview ?? (IReadOnlyList<TenantSupervisionRowDto>)[]);
    }

    public Task<TenantSupervisionDetailDto?> GetTenantDetailAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_throws)
        {
            throw new InvalidOperationException("Échec simulé de la supervision.");
        }

        return Task.FromResult(_detail);
    }

    public Task<bool> AcknowledgeAsync(string tenantId, Guid alertId, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        Acknowledgements.Add((tenantId, alertId, operatorIdentity));
        return Task.FromResult(_ackResult);
    }
}
