namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

/// <summary>Double de <see cref="IServiceDefinitionQueries"/> renvoyant des jeux fixés (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeServiceDefinitionQueries : IServiceDefinitionQueries
{
    private readonly IReadOnlyList<ServiceDefinitionDto> _services;

    public FakeServiceDefinitionQueries(IReadOnlyList<ServiceDefinitionDto>? services = null) => _services = services ?? [];

    public Task<IReadOnlyList<ServiceDefinitionDto>> List(Guid? companyId, CancellationToken ct = default) =>
        Task.FromResult(_services);

    public Task<ServiceDefinitionDto?> GetByCode(string code, Guid? companyId, CancellationToken ct = default) =>
        Task.FromResult(_services.FirstOrDefault(s => s.Code == code));

    public Task<ServiceDefinitionDto?> GetById(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_services.FirstOrDefault(s => s.Id == id) ?? (_services.Count == 0 ? null : _services[0]));
}
