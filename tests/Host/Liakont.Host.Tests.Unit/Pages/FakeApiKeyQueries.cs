namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

/// <summary>Double d'<see cref="IApiKeyQueries"/> renvoyant des jeux fixés (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeApiKeyQueries : IApiKeyQueries
{
    private readonly IReadOnlyList<ApiKeyDto> _keys;

    public FakeApiKeyQueries(IReadOnlyList<ApiKeyDto>? keys = null) => _keys = keys ?? [];

    public Task<IReadOnlyList<ApiKeyDto>> ListByCompany(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(_keys);

    public Task<ApiKeyDto?> GetById(Guid apiKeyId, CancellationToken ct = default) =>
        Task.FromResult(_keys.FirstOrDefault(k => k.Id == apiKeyId) ?? (_keys.Count == 0 ? null : _keys[0]));
}
