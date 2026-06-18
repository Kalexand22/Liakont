namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.Grid;

/// <summary>
/// Double d'<see cref="IGridPreferenceService"/> qui FORCE un jeu de colonnes visibles (tests bUnit RB6 P2).
/// Sert à exercer le rendu d'une colonne <c>Date</c> migrée mais <c>defaultVisible:false</c> dans le registre :
/// la grille retombe sinon sur les colonnes visibles par défaut et le template ne serait jamais rendu.
/// <see cref="UserGridPreference.ColumnKeys"/> = liste ORDONNÉE des colonnes visibles (cf. doc du record).
/// Enregistrer APRÈS <see cref="AdminPageTestServices.AddAdminPageStubs"/> (dernier gagne) pour remplacer le
/// service nul par défaut.
/// </summary>
internal sealed class FakeGridPreferenceService : IGridPreferenceService
{
    private readonly string[] _visibleColumns;

    public FakeGridPreferenceService(params string[] visibleColumns) => _visibleColumns = visibleColumns;

    public Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
        Task.FromResult<UserGridPreference?>(_visibleColumns.Length == 0
            ? null
            : new UserGridPreference(Guid.NewGuid(), userId, gridKey, _visibleColumns, default, null));

    public Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SaveColumnWidthsAsync(Guid userId, string gridKey, IReadOnlyDictionary<string, string> columnWidths, CancellationToken ct = default) =>
        Task.CompletedTask;
}
