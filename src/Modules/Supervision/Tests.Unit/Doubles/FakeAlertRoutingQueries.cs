namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Matrice de routage fictive pour le routage des notifications (FIX212). Par défaut VIDE (⇒ repli
/// modèle simple, comportement antérieur inchangé) ; configurable avec des entrées pour tester le routage.
/// </summary>
internal sealed class FakeAlertRoutingQueries : IAlertRoutingQueries
{
    private readonly IReadOnlyList<AlertRoutingRuleDto> _matrix;

    public FakeAlertRoutingQueries(params AlertRoutingRuleDto[] matrix)
    {
        _matrix = matrix ?? [];
    }

    public Task<IReadOnlyList<AlertRoutingRuleDto>> GetAlertRoutingMatrix(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(_matrix);
}
