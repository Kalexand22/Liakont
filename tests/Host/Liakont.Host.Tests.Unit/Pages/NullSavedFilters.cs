namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.Grid;

internal sealed class NullSavedFilters : ISavedFilterService
{
    public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SavedFilter>>([]);

    public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<SavedFilter?>(null);

    public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default) =>
        Task.FromResult(filter);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

    public Task SetDefaultAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
}
