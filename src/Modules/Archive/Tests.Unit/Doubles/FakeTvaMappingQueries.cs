namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;

/// <summary>Double d'<see cref="ITvaMappingQueries"/> pour le test de réversibilité (API03).</summary>
internal sealed class FakeTvaMappingQueries : ITvaMappingQueries
{
    public Task<MappingTableDto?> GetMappingTable(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<MappingTableDto?>(null);
}
