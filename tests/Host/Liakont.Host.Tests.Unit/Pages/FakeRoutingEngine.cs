namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;

/// <summary>Double d'<see cref="IRoutingEngine"/> no-op (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeRoutingEngine : IRoutingEngine
{
    public Task<IReadOnlyList<RoutingResultDto>> EvaluateRoutingAsync(
        string entityType,
        IReadOnlyDictionary<string, JsonElement> data,
        Guid? companyId = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RoutingResultDto>>([]);
}
