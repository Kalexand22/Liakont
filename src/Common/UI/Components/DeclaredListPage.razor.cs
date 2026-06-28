namespace Stratum.Common.UI.Components;

using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Declarative list page component that absorbs all CRUD list boilerplate:
/// state management, filtering, sorting, paging, selection, bulk actions.
/// Wraps StratumDataGrid, FilterBar, Pagination, PageHeader.
/// When <see cref="ViewModes"/> is set, delegates rendering to StratumListContainer
/// for multi-view support (Table/Card/Kanban/Calendar) with shared state.
/// </summary>
/// <typeparam name="TItem">The DTO type displayed in the grid.</typeparam>
public partial class DeclaredListPage<TItem> : ComponentBase
    where TItem : notnull
{
    private static readonly FilterExpressionBuilder<TItem> _filterExpressionBuilder = new();

    // GFI14 — compile-time LoggerMessage delegates (CA1848). Static so they're
    // allocation-free at the call site and so the message template is validated
    // once at startup instead of on every log call.
    private static readonly Action<ILogger, string, Exception?> _logActorContextUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1401, "GFI14_ActorContextUnavailable"),
            "GFI14: ActorContext unavailable, skipping filter persistence for {GridKey}");

    private static readonly Action<ILogger, string, Exception?> _logFilterSaveFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1402, "GFI14_FilterSaveFailed"),
            "GFI14: failed to persist grid filter state for {GridKey}");

    // GFI06: unified filter state combining global search, simple filters and advanced filter.
    private readonly GridFilterState _filterState = new();

    private IReadOnlyList<TItem> _allItems = [];
    private List<TItem> _filteredItems = [];
    private IReadOnlyList<TItem> _pagedItems = [];
    private HashSet<TItem> _selectedItems = [];
    private IReadOnlyList<TItem> _selectedItemsList = [];
    private IReadOnlyList<string>? _visibleColumnKeys;
    private Func<TItem, bool>? _advancedFilterPredicate;

    // Chip projection + GFI06 popover state.
    private IReadOnlyList<FilterChipModel> _chips = [];
    private bool _simpleBuilderVisible;
    private FilterCriterion? _simpleBuilderInitial;
    private int? _editingSimpleFilterIndex;

    // GFI06 — refs to the inner grid / container so we can route advanced filter
    // chip-edit through their built-in StratumFilterBuilder (single source of truth).
    private StratumDataGrid<TItem>? _gridRef;
    private StratumListContainer<TItem>? _containerRef;

    private int _previousCustomFilterCount;
    private int _previousCustomFilterVersion;
    private string _sortColumn = "Code";
    private SortDirection _sortDirection = SortDirection.Ascending;
    private int _page = 1;
    private int _pageSize = 25;
    private bool _loading;
    private bool _executingBulk;
    private string? _bulkError;

    /// <summary>
    /// True while the user has at least one group column applied via the chooser.
    /// Drives pagination visibility and the Data parameter binding so grouping
    /// aggregates across the entire filtered dataset.
    /// </summary>
    private bool _hasActiveGroups;

    /// <summary>Current active view kind (tracked for pagination visibility).</summary>
    private ViewKind _activeViewKind = ViewKind.Table;

    private Dictionary<string, PropertyInfo>? _propertyCache;

    // GUX03 — persistent selection binding (always active; TItem is used as its own key).
    private PersistentSelectionBinding<TItem, TItem>? _persistentBinding;

    // GUX04 — "voir la sélection" filters the grid in place instead of opening a dialog.
    private bool _viewingSelection;
    private HashSet<TItem> _viewingSelectionSet = [];

    // GFI09 — cell right-click context menu state.
    private bool _cellMenuVisible;
    private double _cellMenuX;
    private double _cellMenuY;
    private string _cellMenuField = string.Empty;
    private object? _cellMenuValue;
    private string _cellMenuDisplay = string.Empty;
    private ElementReference _cellMenuRef;
    private bool _cellMenuShouldFocus;
    private double _cellMenuViewportWidth;

    private double _cellMenuViewportHeight;

    // GUX13 — resolved FK navigation target for the current right-clicked cell.
    // Null when the column does not declare an EntityReference or when the
    // referenced id is empty. Used to gate the "Ouvrir la fiche" menu items.
    private EntityReferenceTarget? _cellMenuEntityTarget;

    // GFI14 — set to true once the initial URL/preference restoration finishes.
    // Handlers skip URL + preference writes until then so the restore pass does
    // not immediately overwrite itself with an empty state from the first render.
    private bool _initialRestoreDone;

    // GFI16 — set when the restore pass produced an active advanced filter that
    // still needs to be pushed into the child grid/container after the first
    // render. OnInitializedAsync runs before the refs exist, so we defer the
    // SyncAdvancedFilterToGrid() call to OnAfterRenderAsync(firstRender: true).
    private bool _pendingInitialAdvancedSync;

    // GFI14 — monotonically incremented before each fire-and-forget persistence
    // call. The save task compares its captured sequence against the latest
    // value and drops its write if another call has already been scheduled,
    // collapsing bursts of rapid edits into a single DB round-trip.
    private int _saveFilterSequence;

    /// <summary>Page h1 title.</summary>
    [Parameter]
    [EditorRequired]
    public string Title { get; set; } = default!;

    /// <summary>Root data-testid attribute.</summary>
    [Parameter]
    [EditorRequired]
    public string TestId { get; set; } = default!;

    /// <summary>Async data loader.</summary>
    [Parameter]
    [EditorRequired]
    public Func<Task<IReadOnlyList<TItem>>> LoadItems { get; set; } = default!;

    /// <summary>URL builder for row activation (navigate to detail).</summary>
    [Parameter]
    [EditorRequired]
    public Func<TItem, string> DetailUrl { get; set; } = default!;

    /// <summary>URL for the "New" button. Null hides the button.</summary>
    [Parameter]
    public string? CreateUrl { get; set; }

    /// <summary>Label for the "New" button.</summary>
    [Parameter]
    public string CreateLabel { get; set; } = "+ Nouveau";

    /// <summary>Permission for the "New" button. Null means no permission check.</summary>
    [Parameter]
    public string? CreatePermission { get; set; }

    /// <summary>Show a PageHeader with title, subtitle and actions. Default false (compact mode).</summary>
    [Parameter]
    public bool ShowHeader { get; set; }

    /// <summary>
    /// Optional subtitle shown under the page title when <see cref="ShowHeader"/> is true.
    /// Default null (no subtitle). The item count is already shown in the pagination footer;
    /// do not set this to a count. Ignored when <see cref="ShowHeader"/> is false.
    /// </summary>
    [Parameter]
    public string? Subtitle { get; set; }

    /// <summary>Search input placeholder.</summary>
    [Parameter]
    public string SearchPlaceholder { get; set; } = "Rechercher\u2026 (raccourci : /)";

    /// <summary>Custom search predicate. Falls back to ColumnRegistry-derived search.</summary>
    [Parameter]
    public Func<TItem, string, bool>? SearchPredicate { get; set; }

    /// <summary>Initial sort column name.</summary>
    [Parameter]
    public string DefaultSortColumn { get; set; } = "Code";

    /// <summary>Initial sort direction.</summary>
    [Parameter]
    public SortDirection DefaultSortDirection { get; set; } = SortDirection.Ascending;

    /// <summary>Initial page size.</summary>
    [Parameter]
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>Manual sort key resolution. Falls back to reflection.</summary>
    [Parameter]
    public Func<TItem, string, object>? SortKeySelector { get; set; }

    /// <summary>Dynamic column registry. Enables advanced filter and column chooser.</summary>
    [Parameter]
    public IColumnRegistry<TItem>? ColumnRegistry { get; set; }

    /// <summary>Templates for registry columns.</summary>
    [Parameter]
    public IDictionary<string, RenderFragment<TItem>>? ColumnTemplates { get; set; }

    /// <summary>Unique key for column/filter preferences persistence.</summary>
    [Parameter]
    public string? GridKey { get; set; }

    /// <summary>Per-row kebab menu actions.</summary>
    [Parameter]
    public IReadOnlyList<GridRowAction<TItem>>? RowActions { get; set; }

    /// <summary>Bulk action configurations.</summary>
    [Parameter]
    public IReadOnlyList<BulkActionConfig<TItem>>? BulkActions { get; set; }

    /// <summary>
    /// GUX03 / FIX302 — opt out of the cross-page persistent selection bar (the floating
    /// "N sélectionné(s) (M au total)" overlay with Ajouter / Retirer / Voir / Vider). Default true
    /// preserves the existing behavior for every page. A page that drives its bulk actions off the
    /// current-page selection only (e.g. Documents) sets this to false so a SINGLE selection bar — the
    /// bulk-actions bar — is shown; otherwise a second, confusing bar appears whose "(0 au total)"
    /// counter never moves because the page never feeds the persistent set.
    /// This parameter is <b>init-only</b>: the value is read once in <see cref="OnInitialized"/> to
    /// create (or skip) the <c>_persistentBinding</c>. Changing it dynamically after the first render
    /// has no effect.
    /// </summary>
    [Parameter]
    public bool EnablePersistentSelection { get; set; } = true;

    /// <summary>Static StratumColumn children (alternative to ColumnRegistry).</summary>
    [Parameter]
    public RenderFragment? Columns { get; set; }

    /// <summary>Custom filter controls for the FilterBar.Filters slot.</summary>
    [Parameter]
    public RenderFragment? CustomFilters { get; set; }

    /// <summary>Additional filter logic beyond search.</summary>
    [Parameter]
    public Func<TItem, bool>? CustomFilterPredicate { get; set; }

    /// <summary>Called when "Clear all" includes custom filters.</summary>
    [Parameter]
    public EventCallback OnCustomFiltersClear { get; set; }

    /// <summary>Number of active custom filters for badge.</summary>
    [Parameter]
    public int ActiveCustomFilterCount { get; set; }

    /// <summary>Version token for custom filter values. Bump to trigger re-filter when values change without count changing.</summary>
    [Parameter]
    public int CustomFilterVersion { get; set; }

    /// <summary>Extra buttons in PageHeader.Actions (only when ShowHeader=true).</summary>
    [Parameter]
    public RenderFragment? HeaderActions { get; set; }

    /// <summary>Custom content in the toolbar end area (next to Filtres/Colonnes/Create button).</summary>
    [Parameter]
    public RenderFragment? ToolbarActions { get; set; }

    /// <summary>Grid ARIA label.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>Enable export buttons in the grid toolbar.</summary>
    [Parameter]
    public bool AllowExport { get; set; }

    /// <summary>Export formats to show. Default: CSV only.</summary>
    [Parameter]
    public ExportFormat ExportFormats { get; set; } = ExportFormat.Csv;

    /// <summary>Base file name for exports.</summary>
    [Parameter]
    public string ExportFileName { get; set; } = "export";

    /// <summary>Custom empty state content.</summary>
    [Parameter]
    public RenderFragment? EmptyContent { get; set; }

    /// <summary>
    /// Enable the grid's toolbar group picker (multi-column grouping).
    /// The picker itself is opt-in per page; pagination and data scope only change
    /// while the user actually has a group hierarchy applied — as long as no group
    /// is selected, the page keeps its normal paged behavior. Default false.
    /// </summary>
    [Parameter]
    public bool AllowGrouping { get; set; }

    /// <summary>Breadcrumb trail.</summary>
    [Parameter]
    public IReadOnlyList<BreadcrumbItem>? Breadcrumbs { get; set; }

    // ── Multi-view parameters (UIX02) ───────────────────────────

    /// <summary>
    /// View mode registry. When set, DeclaredListPage renders via StratumListContainer
    /// with multi-view support (Table/Card/Kanban/Calendar). Default null = table only.
    /// </summary>
    [Parameter]
    public IViewModeRegistry? ViewModes { get; set; }

    /// <summary>Property name for Kanban column grouping (e.g. "Status").</summary>
    [Parameter]
    public string? KanbanGroupByProperty { get; set; }

    /// <summary>Property name for Calendar date axis (e.g. "DueDate").</summary>
    [Parameter]
    public string? CalendarDateProperty { get; set; }

    /// <summary>Property name for Calendar event color (e.g. "Category").</summary>
    [Parameter]
    public string? CalendarColorProperty { get; set; }

    /// <summary>Custom card template for Card view.</summary>
    [Parameter]
    public RenderFragment<TItem>? CardTemplate { get; set; }

    /// <summary>Injected grid preference service for view persistence.</summary>
    [Inject]
    private IGridPreferenceService? PreferenceService { get; set; }

    /// <summary>BUG-19 — mémoire de circuit de l'ordre affiché, pour la navigation précédent/suivant en vue détail.</summary>
    [Inject]
    private Navigation.IListNavigationContext? ListNavigation { get; set; }

    /// <summary>GUX03 — session-scoped persistent selection service (TItem is its own key).</summary>
    [Inject]
    private IPersistentSelectionService<TItem> PersistentSelectionService { get; set; } = default!;

    /// <summary>Whether multi-view mode is active.</summary>
    private bool IsMultiView => ViewModes is not null;

    /// <summary>
    /// True when at least one declared bulk action is GLOBAL (<see cref="BulkActionConfig{TItem}.RequiresSelection"/>
    /// = false — e.g. a "recheck everything in the current scope" action, FIX207). Such actions keep the selection
    /// bar visible even with no rows selected, so the operator can trigger them at any time; selection-scoped
    /// actions (the default) stay gated on a selection.
    /// </summary>
    private bool HasGlobalBulkActions => BulkActions is not null && BulkActions.Any(static a => !a.RequiresSelection);

    // Current global search value (kept as a shortcut property for templates).
    private string SearchValue => _filterState.GlobalSearch ?? string.Empty;

    // True when the page currently has any filter active (used for the result counter DF-06).
    private bool HasAnyFilter =>
        _filterState.HasActiveFilters || ActiveCustomFilterCount > 0;

    protected override void OnInitialized()
    {
        _sortColumn = DefaultSortColumn;
        _sortDirection = DefaultSortDirection;
        _pageSize = DefaultPageSize;

        // GUX03 — using TItem as its own key keeps DeclaredListPage single-typed.
        // DTOs loaded in one LoadItems() call keep stable references for the session.
        // FIX302 — a page can opt out (EnablePersistentSelection=false): leave the binding null so the
        // grid renders no persistent selection bar (its "(0 au total)" counter is meaningless on pages
        // that drive bulk actions off the current-page selection only).
        if (EnablePersistentSelection)
        {
            _persistentBinding = new PersistentSelectionBinding<TItem, TItem>(PersistentSelectionService, item => item);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await RestoreFilterStateAsync();
        }
        finally
        {
            // Always arm the persistence path, even if RestoreFilterStateAsync threw.
            // Otherwise a transient restore failure would silently disable URL sync
            // and preference writes for the entire lifetime of the page.
            _initialRestoreDone = true;
        }

        await LoadAsync();

        // GFI14 DF-02: if the restore produced active filters (from URL or prefs),
        // make the URL match. For URL-sourced filters BuildUriWithFilters is a no-op;
        // for preference-sourced filters it publishes them back into the query string
        // so the link is immediately shareable.
        if (_filterState.HasActiveFilters)
        {
            PersistAndSyncFilterState();
        }

        // GFI16: the child grid/container refs are null until after the first
        // render, so any advanced filter reconstructed during restore has to be
        // pushed to them in OnAfterRenderAsync — otherwise opening the advanced
        // builder starts from an empty filter and its next Apply wipes the
        // restored state.
        if (_filterState.AdvancedFilter is not null)
        {
            _pendingInitialAdvancedSync = true;
        }
    }

    protected override void OnParametersSet()
    {
        if (_previousCustomFilterCount != ActiveCustomFilterCount
            || _previousCustomFilterVersion != CustomFilterVersion)
        {
            _previousCustomFilterCount = ActiveCustomFilterCount;
            _previousCustomFilterVersion = CustomFilterVersion;
            _page = 1;

            // Clear selection so bulk actions cannot operate on rows hidden by the new filter.
            ClearSelectionState();
            ApplyFilters();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _pendingInitialAdvancedSync)
        {
            _pendingInitialAdvancedSync = false;
            SyncAdvancedFilterToGrid();
        }

        if (_cellMenuShouldFocus && _cellMenuVisible)
        {
            _cellMenuShouldFocus = false;
            try
            {
                await _cellMenuRef.FocusAsync();
            }
            catch
            {
                // Element may not be mounted — non-critical.
            }
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _allItems = await LoadItems();
            ApplyFilters();
        }
        finally
        {
            _loading = false;
        }
    }

    // GFI14 — restore the last filter state for this grid. URL query parameters
    // win (shareable deep links are self-contained and deterministic); we only
    // fall back to the per-user persisted blob when the URL carries no filters.
    // The URL format never carries sub-groups, so a shareable state must be
    // fully flat (SimpleFilterUrlSerializer emits nothing when sub-groups are
    // present). A share-link round trip therefore never silently mixes the
    // receiver's saved advanced branch with criteria from the URL (GFI16).
    private async Task RestoreFilterStateAsync()
    {
        GridFilterState? restored = null;

        // Extract the query slice manually — NavigationManager only exposes the full Uri,
        // and Uri.Query would throw on a non-absolute value during prerender.
        // A URL like "/page#foo?bar" has its '?' inside the fragment (hashStart < queryStart);
        // per RFC 3986 that is NOT a query string and must be ignored.
        var uri = Nav.Uri ?? string.Empty;
        string? query = null;
        var queryStart = uri.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            var hashStart = uri.IndexOf('#', StringComparison.Ordinal);
            if (hashStart < 0 || hashStart > queryStart)
            {
                var queryEnd = hashStart < 0 ? uri.Length : hashStart;
                query = uri.Substring(queryStart, queryEnd - queryStart);
            }
        }

        var urlFilters = SimpleFilterUrlSerializer.Parse(query);
        if (urlFilters.Count > 0)
        {
            restored = new GridFilterState();
            foreach (var criterion in urlFilters)
            {
                restored.AddSimpleFilter(criterion);
            }
        }

        if (restored is null && PreferenceService is not null && !string.IsNullOrWhiteSpace(GridKey))
        {
            Guid userIdForRestore;
            try
            {
                userIdForRestore = ActorContext.Current.UserId;
            }
            catch (Exception)
            {
                // ActorContext may be unresolved during prerender or outside an
                // authenticated scope — treat as "no user" rather than crashing.
                userIdForRestore = Guid.Empty;
            }

            if (userIdForRestore != Guid.Empty)
            {
                try
                {
                    var pref = await PreferenceService.GetPreferenceAsync(userIdForRestore, GridKey!);
                    if (pref?.FilterStateJson is { Length: > 0 } json)
                    {
                        restored = GridFilterStateSerializer.Deserialize(json);
                    }
                }
                catch (Exception)
                {
                    // Persistence lookup failed — render with an empty filter state rather
                    // than crashing the page on a stale or corrupted preference row.
                }
            }
        }

        if (restored is null || !restored.HasActiveFilters)
        {
            return;
        }

        // GFI14 security: restrict the restored flat root criteria to the grid's
        // declared column set. Otherwise a crafted deep link or a stale preference
        // row could filter on properties the UI never exposes (audit refs, internal
        // flags, tokens) and use the narrowing effect as an oracle to probe their
        // values. When no ColumnRegistry is provided we fall back to the
        // reflection-based property whitelist — same guarantee, derived from the
        // DTO surface. Nested sub-groups are left untouched to preserve existing
        // GFI14 behavior (only "simple filters" were ever whitelisted).
        var allowedFields = BuildAllowedFilterFields();

        _filterState.GlobalSearch = restored.GlobalSearch;

        // GFI16 — apply the GFI14 field whitelist to the flat root criteria of
        // the restored filter tree. Sub-groups are preserved verbatim (same
        // coverage as the pre-GFI16 code, which only pruned the "simple
        // filters" list). When no whitelist is available (no registry, no
        // properties), accept the restored filter as-is.
        var restoredAdvanced = restored.AdvancedFilter;
        if (restoredAdvanced is not null && allowedFields is not null)
        {
            var pruned = new List<FilterCriterion>(restoredAdvanced.Criteria.Count);
            foreach (var c in restoredAdvanced.Criteria)
            {
                if (allowedFields.Contains(c.Field))
                {
                    pruned.Add(c);
                }
            }

            var hasSubGroups = restoredAdvanced.SubGroups is { Count: > 0 };
            restoredAdvanced = (pruned.Count == 0 && !hasSubGroups)
                ? null
                : new FilterGroup(restoredAdvanced.Logic, pruned, restoredAdvanced.SubGroups);
        }

        _filterState.AdvancedFilter = restoredAdvanced;

        // Any expression-builder failure (unknown field after model rename, type
        // mismatch from a stale preference blob, unsupported operator …) must NOT
        // crash the initial render — fall back to "no filter" and let the page load.
        try
        {
            RebuildAdvancedFilterPredicate();
        }
        catch (Exception)
        {
            _filterState.AdvancedFilter = null;
            _advancedFilterPredicate = null;
        }
    }

    // GFI14 — build the set of field names a restored filter is allowed to target.
    // Prefers ColumnRegistry.Key values (the user-facing column set) and falls back
    // to the declared TItem properties when no registry is provided.
    private HashSet<string>? BuildAllowedFilterFields()
    {
        if (ColumnRegistry is not null)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in ColumnRegistry.GetAvailableColumns())
            {
                if (!string.IsNullOrEmpty(col.Key))
                {
                    set.Add(col.Key);
                }

                if (!string.IsNullOrEmpty(col.Property))
                {
                    set.Add(col.Property);
                }
            }

            return set;
        }

        // Reflection-based fallback: only public instance properties on TItem are
        // addressable by the expression builder, and only ones the UI could have
        // produced a chip for. This is still narrower than the 'anything' state
        // that existed before GFI14.
        var props = typeof(TItem).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        if (props.Length == 0)
        {
            return null;
        }

        var fallback = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            fallback.Add(p.Name);
        }

        return fallback;
    }

    // GFI14 — push the current filter state into the URL query string and the
    // per-user preference row. Fires and forgets the persistence call; URL sync
    // runs synchronously so the browser bar matches the UI immediately.
    private void PersistAndSyncFilterState()
    {
        if (!_initialRestoreDone)
        {
            return;
        }

        try
        {
            var newUri = SimpleFilterUrlSerializer.BuildUriWithFilters(Nav.Uri, _filterState);
            if (!string.Equals(newUri, Nav.Uri, StringComparison.Ordinal))
            {
                Nav.NavigateTo(newUri, forceLoad: false, replace: true);
            }
        }
        catch (Exception)
        {
            // URL update failure is non-fatal — the grid still works without it.
        }

        if (PreferenceService is null || string.IsNullOrWhiteSpace(GridKey))
        {
            return;
        }

        Guid userId;
        try
        {
            userId = ActorContext.Current.UserId;
        }
        catch (Exception ex)
        {
            // Same guard as RestoreFilterStateAsync: do not let an unresolved
            // ActorContext permanently disable persistence for this page.
            _logActorContextUnavailable(Logger, GridKey!, ex);
            return;
        }

        if (userId == Guid.Empty)
        {
            return;
        }

        var json = GridFilterStateSerializer.Serialize(_filterState);
        var seq = System.Threading.Interlocked.Increment(ref _saveFilterSequence);
        _ = SaveFilterStateSafelyAsync(userId, GridKey!, json, seq);
    }

    private async Task SaveFilterStateSafelyAsync(Guid userId, string gridKey, string? json, int seq)
    {
        // Coalesce rapid edits: if another PersistAndSyncFilterState call has
        // already been scheduled after us, drop this write. The later call
        // carries the current state and will overwrite what we'd have written.
        if (System.Threading.Volatile.Read(ref _saveFilterSequence) != seq)
        {
            return;
        }

        try
        {
            await PreferenceService!.SaveFilterStateAsync(userId, gridKey, json);
        }
        catch (Exception ex)
        {
            // Best-effort persistence — never surface DB errors from the render
            // loop, but do not swallow silently: oncall needs a breadcrumb.
            _logFilterSaveFailed(Logger, gridKey, ex);
        }
    }

    private void ApplyFilters()
    {
        IEnumerable<TItem> result = _allItems;

        // GFI06 DF-02: compile the three filter sources in AND.
        // 1. Global search (FilterBar text input, synced with FilterChipBar "search" chip)
        var search = _filterState.GlobalSearch;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var query = search.Trim();
            if (SearchPredicate is not null)
            {
                result = result.Where(item => SearchPredicate(item, query));
            }
            else if (ColumnRegistry is not null)
            {
                var fields = ColumnRegistry.GetSearchableFields(_visibleColumnKeys);
                result = result.Where(item => MatchesRegistrySearch(item, query, fields));
            }
        }

        // 2. Page-specific custom filters (legacy FilterBar.Filters slot)
        if (CustomFilterPredicate is not null)
        {
            result = result.Where(CustomFilterPredicate);
        }

        // 3. Unified filter tree (GFI16): simple chips and advanced groups now
        // share a single FilterGroup. The predicate is recompiled whenever the
        // tree mutates (HandleAdvancedFilterChanged / HandleSimpleBuilderApply /
        // chip remove / cell menu add).
        if (_advancedFilterPredicate is not null)
        {
            result = result.Where(_advancedFilterPredicate);
        }

        // 4. GUX04 — "voir la sélection" mode: restrict to the persisted selection snapshot.
        if (_viewingSelection && _viewingSelectionSet.Count > 0)
        {
            result = result.Where(_viewingSelectionSet.Contains);
        }

        result = ApplySort(result);

        _filteredItems = result.ToList();
        ApplyPaging();
        RefreshChips();
    }

    private void RefreshChips()
    {
        _chips = FilterChipProjector.Project(_filterState);
    }

    // GFI16 — recompile the unified filter predicate after any mutation to
    // _filterState.AdvancedFilter. Null result means "no predicate applied";
    // callers MUST call this before ApplyFilters when they have touched the
    // filter tree outside of HandleAdvancedFilterChanged.
    private void RebuildAdvancedFilterPredicate()
    {
        var group = _filterState.AdvancedFilter;
        if (group is null)
        {
            _advancedFilterPredicate = null;
            return;
        }

        try
        {
            _advancedFilterPredicate = _filterExpressionBuilder.Build(group).Compile();
            return;
        }
        catch (Exception)
        {
            // Full-tree compile failed (e.g. a stale nested sub-group that
            // references a renamed field). Fall through to the salvage path
            // below so the valid root criteria still filter the page.
        }

        // GFI16 P1 salvage: when the root is AND and has valid criteria, try
        // to compile those alone without the (broken) sub-groups. Otherwise
        // the whole predicate would silently drop to null and the page would
        // show restored chips that filter nothing.
        var hasSubGroups = group.SubGroups is { Count: > 0 };
        if (hasSubGroups && group.Logic == FilterLogic.And && group.Criteria.Count > 0)
        {
            try
            {
                var rootOnly = new FilterGroup(FilterLogic.And, group.Criteria);
                _advancedFilterPredicate = _filterExpressionBuilder.Build(rootOnly).Compile();
                return;
            }
            catch (Exception)
            {
                // Root-only compile failed too — give up and render with no
                // filter applied rather than crashing the render loop.
            }
        }

        _advancedFilterPredicate = null;
    }

    private bool MatchesRegistrySearch(TItem item, string query, IReadOnlyList<string> fields)
    {
        _propertyCache ??= new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        var type = typeof(TItem);

        foreach (var field in fields)
        {
            if (!_propertyCache.TryGetValue(field, out var prop))
            {
                prop = type.GetProperty(field, BindingFlags.Public | BindingFlags.Instance);
                if (prop is not null)
                {
                    _propertyCache[field] = prop;
                }
            }

            if (prop is null)
            {
                continue;
            }

            var value = prop.GetValue(item)?.ToString();
            if (value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<TItem> ApplySort(IEnumerable<TItem> source)
    {
        Func<TItem, object> keySelector;

        if (SortKeySelector is not null)
        {
            var col = _sortColumn;
            keySelector = item => SortKeySelector(item, col);
        }
        else
        {
            var prop = typeof(TItem).GetProperty(_sortColumn, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null)
            {
                return source;
            }

            keySelector = item => prop.GetValue(item) ?? string.Empty;
        }

        return _sortDirection == SortDirection.Ascending
            ? source.OrderBy(keySelector)
            : source.OrderByDescending(keySelector);
    }

    private void ApplyPaging()
    {
        _pagedItems = _filteredItems
            .Skip((_page - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();
    }

    private Task HandleSearchAsync(string value)
    {
        _filterState.GlobalSearch = value;
        _page = 1;
        ClearSelectionState();
        ApplyFilters();
        PersistAndSyncFilterState();
        return Task.CompletedTask;
    }

    private Task HandleSortChangedAsync(SortChangedArgs args)
    {
        _sortColumn = args.Column;
        _sortDirection = args.Direction;
        ClearSelectionState();
        ApplyFilters();
        return Task.CompletedTask;
    }

    private void HandleSelectionChanged(IReadOnlyList<TItem> items)
    {
        _selectedItems = new HashSet<TItem>(items);
        _selectedItemsList = items;
        _bulkError = null;
    }

    private void HandleRowActivated(TItem item)
    {
        CaptureListNavigationContext();
        Nav.NavigateTo(DetailUrl(item));
    }

    /// <summary>
    /// BUG-19 — mémorise l'ORDRE AFFICHÉ (filtré + trié, toutes pages : <see cref="_filteredItems"/>) sous forme
    /// d'URLs de détail, pour la navigation précédent/suivant en vue détail (<c>RecordNavigator</c>). Appelée au
    /// moment où l'opérateur ouvre une fiche. Sans effet si la liste n'a pas de vue détail (<see cref="DetailUrl"/>
    /// nul) ou si le service n'est pas enregistré (rendu hors <c>AddCommonUI</c>). Lecture seule.
    /// </summary>
    private void CaptureListNavigationContext()
    {
        if (DetailUrl is null || ListNavigation is null || _filteredItems.Count == 0)
        {
            return;
        }

        var urls = new List<string>(_filteredItems.Count);
        foreach (var item in _filteredItems)
        {
            urls.Add(DetailUrl(item));
        }

        ListNavigation.Capture(urls);
    }

    private Task HandlePageChangedAsync(int page)
    {
        _page = page;
        ClearSelectionState();
        ApplyPaging();
        return Task.CompletedTask;
    }

    private Task HandlePageSizeChangedAsync(int size)
    {
        _pageSize = size;
        _page = 1;
        ClearSelectionState();
        ApplyPaging();
        return Task.CompletedTask;
    }

    private void HandleVisibleColumnsChanged(IReadOnlyList<string> keys)
    {
        _visibleColumnKeys = keys;
        ApplyFilters();
    }

    private Task HandleAdvancedFilterChanged(FilterGroup? filter)
    {
        _filterState.AdvancedFilter = filter;
        RebuildAdvancedFilterPredicate();

        _page = 1;
        ClearSelectionState();
        ApplyFilters();
        PersistAndSyncFilterState();
        return Task.CompletedTask;
    }

    // ── GFI06: chip bar + simple builder handlers ───────────────
    private void HandleAddFilter()
    {
        // SimpleFilterBuilder requires a column registry to render the field picker
        // and the value editor. Without it the popover has nothing to render, so the
        // "+ Ajouter un filtre" action is a no-op.
        if (ColumnRegistry is null)
        {
            return;
        }

        _editingSimpleFilterIndex = null;
        _simpleBuilderInitial = null;
        _simpleBuilderVisible = true;
    }

    private async Task HandleClearAllChips()
    {
        _filterState.ClearAll();
        _advancedFilterPredicate = null;
        _page = 1;
        ClearSelectionState();
        SyncAdvancedFilterToGrid();
        if (OnCustomFiltersClear.HasDelegate)
        {
            await OnCustomFiltersClear.InvokeAsync();
        }

        ApplyFilters();
        PersistAndSyncFilterState();
    }

    private void HandleRemoveChip(int chipIndex)
    {
        if (chipIndex < 0 || chipIndex >= _chips.Count)
        {
            return;
        }

        var chip = _chips[chipIndex];
        switch (chip.Source)
        {
            case FilterSource.GlobalSearch:
                _filterState.GlobalSearch = null;
                break;
            case FilterSource.Simple:
                {
                    // Remove by positional index — FilterCriterion uses value equality,
                    // so RemoveSimpleFilter(chip.Criterion) would drop the wrong entry
                    // when two criteria share the same (Field, Operator, Value).
                    var simpleIdx = ResolveSimpleIndex(chipIndex);
                    if (simpleIdx >= 0)
                    {
                        _filterState.RemoveSimpleFilterAt(simpleIdx);
                        RebuildAdvancedFilterPredicate();
                    }

                    break;
                }

            case FilterSource.Advanced:
                // Summary chip for the advanced portion of the tree. When the
                // root is AND, simple chips already cover the additive root
                // criteria and the summary represents only the sub-group tree,
                // so removing it strips the sub-groups and keeps root criteria.
                // When the root is OR, the summary represents the whole tree.
                {
                    var current = _filterState.AdvancedFilter;
                    if (current is null)
                    {
                        _advancedFilterPredicate = null;
                        break;
                    }

                    if (current.Logic == FilterLogic.And && current.Criteria.Count > 0)
                    {
                        _filterState.AdvancedFilter = new FilterGroup(
                            FilterLogic.And,
                            current.Criteria,
                            Array.Empty<FilterGroup>());
                    }
                    else
                    {
                        _filterState.AdvancedFilter = null;
                    }

                    RebuildAdvancedFilterPredicate();
                    break;
                }
        }

        _page = 1;
        ClearSelectionState();
        SyncAdvancedFilterToGrid();
        ApplyFilters();
        PersistAndSyncFilterState();
    }

    // GFI16 chip projection order: [GlobalSearch?, SimpleFilters[0..N]]. The
    // only non-simple chip is the nested-advanced summary chip, which has no
    // per-criterion index, so there is no "advanced index" helper anymore.
    private int ResolveSimpleIndex(int chipIndex)
    {
        var offset = string.IsNullOrWhiteSpace(_filterState.GlobalSearch) ? 0 : 1;
        var idx = chipIndex - offset;
        return idx >= 0 && idx < _filterState.SimpleFilters.Count ? idx : -1;
    }

    // Pushes the current _filterState.AdvancedFilter into the inner grid/container so
    // its internal _activeFilter mirrors what DeclaredListPage has applied. Used when
    // the chip bar mutates the advanced filter (remove / clear) — otherwise the grid's
    // toolbar badge and its built-in filter builder would show stale state.
    private void SyncAdvancedFilterToGrid()
    {
        var filter = _filterState.AdvancedFilter;
        _gridRef?.SetActiveFilterSilently(filter);
        _containerRef?.SetActiveFilterSilently(filter);
    }

    private async Task OpenGridAdvancedFilterBuilderAsync()
    {
        if (_gridRef is not null)
        {
            await _gridRef.OpenAdvancedFilterBuilderAsync();
            return;
        }

        if (_containerRef is not null)
        {
            await _containerRef.OpenAdvancedFilterBuilderAsync();
        }
    }

    private async Task HandleEditChip(int chipIndex)
    {
        if (chipIndex < 0 || chipIndex >= _chips.Count)
        {
            return;
        }

        // Both editors (simple + advanced builder) need the column registry.
        if (ColumnRegistry is null)
        {
            return;
        }

        var chip = _chips[chipIndex];
        switch (chip.Source)
        {
            case FilterSource.Simple:
                if (chip.Criterion is null)
                {
                    return;
                }

                _editingSimpleFilterIndex = ResolveSimpleIndex(chipIndex);
                _simpleBuilderInitial = chip.Criterion;
                _simpleBuilderVisible = true;
                break;

            case FilterSource.Advanced:
                // DF-02: route the chip-edit through the grid's own advanced filter
                // builder so there is a single source of truth for the advanced filter
                // state (no dual StratumFilterBuilder instances → no silent overwrites).
                await OpenGridAdvancedFilterBuilderAsync();
                break;

            case FilterSource.GlobalSearch:
                // Editing the search chip is meaningless — users type into the search box.
                break;
        }
    }

    private void HandleSimpleBuilderApply(FilterCriterion criterion)
    {
        if (_editingSimpleFilterIndex is int idx && idx >= 0 && idx < _filterState.SimpleFilters.Count)
        {
            // In-place replace preserves chip order (the edited chip stays at its
            // original position instead of jumping to the end of the bar).
            _filterState.ReplaceSimpleFilterAt(idx, criterion);
        }
        else
        {
            _filterState.AddSimpleFilter(criterion);
        }

        _simpleBuilderVisible = false;
        _simpleBuilderInitial = null;
        _editingSimpleFilterIndex = null;

        // GFI16: simple writes land in _filterState.AdvancedFilter, so the
        // advanced predicate is the single thing to rebuild — and the inner
        // grid/container must be told so its badge + builder reflect the new
        // state (so opening the advanced builder shows the simple criterion).
        RebuildAdvancedFilterPredicate();
        SyncAdvancedFilterToGrid();
        _page = 1;
        ClearSelectionState();
        ApplyFilters();
        PersistAndSyncFilterState();
    }

    private void HandleSimpleBuilderCancel()
    {
        _simpleBuilderVisible = false;
        _simpleBuilderInitial = null;
        _editingSimpleFilterIndex = null;
    }

    private Task HandleGroupsChangedAsync(IReadOnlyList<string> keys)
    {
        var wasActive = _hasActiveGroups;
        _hasActiveGroups = keys.Count > 0;

        // When exiting grouped mode we slice back to page 1, so any rows selected
        // on higher pages would become invisible. Clear selection to stay consistent
        // with the search/sort/page handlers and avoid bulk actions targeting hidden rows.
        if (wasActive && !_hasActiveGroups)
        {
            _page = 1;
            ClearSelectionState();
            ApplyPaging();
        }

        return Task.CompletedTask;
    }

    private async Task ExecuteBulkAsync(BulkActionConfig<TItem> action)
    {
        if (action.Execute is null)
        {
            return;
        }

        _executingBulk = true;
        _bulkError = null;
        var items = action.ItemFilter is not null
            ? _selectedItems.Where(action.ItemFilter).ToList()
            : _selectedItems.ToList();

        try
        {
            await action.Execute(items);

            // Une action qui DIFFÈRE l'opération à une confirmation explicite (SuppressSuccessToast) ne doit pas
            // afficher de toast « traité(s) » dès le retour d'Execute : ce serait trompeur (rien n'est encore fait).
            if (!action.SuppressSuccessToast)
            {
                ToastService.Show($"{items.Count} élément(s) traité(s).", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            _bulkError = $"Erreur : {ex.Message}";
        }
        finally
        {
            _executingBulk = false;
            ClearSelectionState();
            await LoadAsync();
        }
    }

    private void ClearSelection()
    {
        ClearSelectionState();
        _bulkError = null;
    }

    private void ClearSelectionState()
    {
        _selectedItems.Clear();
        _selectedItemsList = [];
    }

    // ── GUX03/04 — persistent selection handlers ────────────────

    // GUX04: "voir la sélection" filters the grid in place to show only persisted items.
    private void HandleViewPersistentSelection()
    {
        _viewingSelectionSet = new HashSet<TItem>(PersistentSelectionService.Snapshot());
        _viewingSelection = true;
        _page = 1;
        ClearSelectionState();
        ApplyFilters();
    }

    private void ExitViewSelection()
    {
        _viewingSelection = false;
        _viewingSelectionSet = [];
        _page = 1;
        ClearSelectionState();
        ApplyFilters();
    }

    // ── Multi-view handlers (UIX02) ─────────────────────────────
    private void HandleViewChanged(ViewKind kind)
    {
        _activeViewKind = kind;

        // Reset paging when switching back to table
        if (kind == ViewKind.Table)
        {
            _page = 1;
            ApplyPaging();
        }
    }

    private void HandleMultiViewSelectionChanged(IReadOnlyList<TItem> items)
    {
        _selectedItems = new HashSet<TItem>(items);
        _selectedItemsList = items;
        _bulkError = null;
        StateHasChanged();
    }

    private void HandleMultiViewRowActivated(TItem item)
    {
        CaptureListNavigationContext();
        Nav.NavigateTo(DetailUrl(item));
    }

    // ── GFI09 — cell right-click context menu ─────────────────────────
    private async Task HandleCellContextMenu(GridCellContextMenuArgs<TItem> args)
    {
        _cellMenuField = args.Field;
        _cellMenuValue = args.Value;
        _cellMenuDisplay = args.DisplayValue;
        _cellMenuEntityTarget = args.EntityReferenceTarget;
        _cellMenuX = args.ClientX;
        _cellMenuY = args.ClientY;

        try
        {
            var vp = await JS.InvokeAsync<ViewportSize>("stratumUI.getViewport");
            _cellMenuViewportWidth = vp.Width;
            _cellMenuViewportHeight = vp.Height;
        }
        catch
        {
            _cellMenuViewportWidth = 0;
            _cellMenuViewportHeight = 0;
        }

        _cellMenuVisible = true;
        _cellMenuShouldFocus = true;
        StateHasChanged();
    }

    private void HandleCellMenuKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _cellMenuVisible = false;
            _cellMenuEntityTarget = null;
        }
    }

    private string GetCellMenuStyle()
    {
        var x = _cellMenuX;
        var y = _cellMenuY;
        if (_cellMenuViewportWidth > 0)
        {
            x = Math.Max(0, Math.Min(x, _cellMenuViewportWidth - 220));
        }

        if (_cellMenuViewportHeight > 0)
        {
            y = Math.Max(0, Math.Min(y, _cellMenuViewportHeight - 180));
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return $"position:fixed;left:{x.ToString(invariant)}px;top:{y.ToString(invariant)}px;";
    }

    private void HandleCellMenuBackdropClick()
    {
        _cellMenuVisible = false;
        _cellMenuEntityTarget = null;
    }

    private void HandleCellMenuFilterOn()
    {
        var criterion = new FilterCriterion(_cellMenuField, FilterOperator.Equals, _cellMenuValue);
        ApplyCellMenuCriterion(criterion);
    }

    private void HandleCellMenuExclude()
    {
        var criterion = new FilterCriterion(_cellMenuField, FilterOperator.NotEquals, _cellMenuValue);
        ApplyCellMenuCriterion(criterion);
    }

    // GUX13 — navigate to the referenced entity in the current tab.
    private void HandleCellMenuOpenEntity()
    {
        var target = _cellMenuEntityTarget;
        _cellMenuVisible = false;
        _cellMenuEntityTarget = null;

        if (target is null)
        {
            return;
        }

        // INTERDICTION ABSOLUE target="_blank" (GUX13 spec) — this path
        // navigates the current tab via Blazor NavigationManager only.
        Nav.NavigateTo(target.BuildUrl());
    }

    // GUX13 — open the referenced entity in a new Stratum tab via
    // ITabManagerService. We never use target="_blank" and never call
    // window.open, so the navigation stays inside the circuit.
    private void HandleCellMenuOpenEntityInNewTab()
    {
        var target = _cellMenuEntityTarget;
        var display = _cellMenuDisplay;
        _cellMenuVisible = false;
        _cellMenuEntityTarget = null;

        if (target is null)
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(display)
            ? $"{target.Entity} · {target.Id}"
            : display;

        var opened = TabManager.OpenTab(target.BuildUrl(), title);
        if (!opened)
        {
            // OpenTab returns false when the max-tabs limit is reached.
            // Surface the hard cap rather than silently losing the click.
            ToastService.Show(L["CellMenu.OpenEntityTabLimit"], Severity.Warning);
        }
    }

    private async Task HandleCellMenuCopy()
    {
        _cellMenuVisible = false;
        _cellMenuEntityTarget = null;

        try
        {
            var ok = await JS.InvokeAsync<bool>("stratumUI.copyToClipboard", _cellMenuDisplay);
            ToastService.Show(
                ok ? L["CellMenu.CopySuccess"] : L["CellMenu.CopyFailed"],
                ok ? Severity.Success : Severity.Warning);
        }
        catch (JSException)
        {
            ToastService.Show(L["CellMenu.CopyFailed"], Severity.Warning);
        }
        catch (Microsoft.JSInterop.JSDisconnectedException)
        {
            // Circuit disconnected — non-critical
        }
    }

    private void ApplyCellMenuCriterion(FilterCriterion criterion)
    {
        _cellMenuVisible = false;
        _cellMenuEntityTarget = null;

        _filterState.AddSimpleFilter(criterion);
        RebuildAdvancedFilterPredicate();
        SyncAdvancedFilterToGrid();
        _page = 1;
        ClearSelectionState();
        ApplyFilters();
        PersistAndSyncFilterState();
    }

    private sealed record ViewportSize(double Width, double Height);
}
