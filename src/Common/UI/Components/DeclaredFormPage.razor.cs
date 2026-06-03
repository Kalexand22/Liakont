namespace Stratum.Common.UI.Components;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Models;

public partial class DeclaredFormPage<TEntity> : ComponentBase, IDisposable
{
    private Guid _loadedId;
    private bool _loading = true;
    private bool _saving;
    private FormMode _mode;
    private FormMode? _appliedInitialMode;
    private IReadOnlyList<ScopeBinding> _shortcuts = [];

    [Parameter]
    [EditorRequired]
    public Func<bool, string> Title { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public string TestId { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public string ListUrl { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public Func<Guid, Task<TEntity?>> LoadEntity { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public Func<bool> Validate { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public Action ClearErrors { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public Action<TEntity> MapEntity { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public Action ResetFields { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public RenderFragment<FormPageContext> Content { get; set; } = default!;

    [Parameter]
    public Guid? Id { get; set; }

    [Parameter]
    public string? EntityType { get; set; }

    [Parameter]
    public Func<Guid, string>? DetailUrl { get; set; }

    /// <summary>URL for "Save and New" navigation. When set, the SaveAndNew button is shown.</summary>
    [Parameter]
    public string? CreateUrl { get; set; }

    /// <summary>When true, edit mode starts as View (read-only) with an "Éditer" button.</summary>
    [Parameter]
    public bool EnableViewMode { get; set; }

    /// <summary>
    /// Forces the initial mode on load (overrides the EnableViewMode default).
    /// Useful for deep-link routes like /{id}/edit that must open directly in Edit.
    /// Ignored in Create mode.
    /// </summary>
    [Parameter]
    public FormMode? InitialMode { get; set; }

    /// <summary>Permission required to see the "Éditer" button (and thus to enter Edit mode).</summary>
    [Parameter]
    public string? EditPermission { get; set; }

    [Parameter]
    public Func<Task<Guid>>? CreateEntity { get; set; }

    [Parameter]
    public Func<Task>? UpdateEntity { get; set; }

    [Parameter]
    public Action<string>? MapDomainError { get; set; }

    [Parameter]
    public Func<EntityChangedEvent, Task>? OnEntityChanged { get; set; }

    /// <summary>Additional header actions. Receives the form context so the caller can condition actions on mode.</summary>
    [Parameter]
    public RenderFragment<FormPageContext>? HeaderActions { get; set; }

    /// <summary>Status slot rendered next to the page title (e.g. StatusBadge).</summary>
    [Parameter]
    public RenderFragment? StatusContent { get; set; }

    /// <summary>Optional audit section rendered above the action bar (e.g. created/updated dates).</summary>
    [Parameter]
    public RenderFragment? AuditSection { get; set; }

    [Parameter]
    public IReadOnlyList<BreadcrumbItem>? Breadcrumbs { get; set; }

    [Parameter]
    public IReadOnlyList<ScopeBinding>? AdditionalShortcuts { get; set; }

    [Parameter]
    public string? GlobalError { get; set; }

    [Parameter]
    public EventCallback<string?> GlobalErrorChanged { get; set; }

    [Parameter]
    public bool IsDirty { get; set; }

    [Parameter]
    public EventCallback<bool> IsDirtyChanged { get; set; }

    [Inject]
    private IPermissionService PermissionService { get; set; } = default!;

    private bool IsCreateMode => Id is null;

    private bool CanEdit => EditPermission is null || PermissionService.HasPermission(EditPermission);

    private string CancelUrl => IsCreateMode
        ? ListUrl
        : DetailUrl is not null ? DetailUrl(Id!.Value) : $"{ListUrl}/{Id}";

    private FormPageContext Context => new(IsCreateMode, _saving, _loading, _mode);

    /// <summary>Reloads the entity from the backend. Caller can invoke via @ref after external mutations.</summary>
    public async Task ReloadAsync()
    {
        if (IsCreateMode || Id is null)
        {
            return;
        }

        await LoadAsync();
        StateHasChanged();
    }

    public void Dispose()
    {
        PermissionService.OnPermissionsChanged -= HandlePermissionsChanged;
        GC.SuppressFinalize(this);
    }

    protected override void OnInitialized()
    {
        PermissionService.OnPermissionsChanged += HandlePermissionsChanged;
        RebuildShortcuts();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!IsCreateMode && !_saving && Id!.Value != _loadedId)
        {
            _loadedId = Id!.Value;
            _appliedInitialMode = InitialMode;
            _mode = ResolveInitialMode();
            await LoadAsync();
        }
        else if (!IsCreateMode && !_saving && InitialMode != _appliedInitialMode)
        {
            // Same Id but the route template flipped (e.g. /{id}/edit → /{id} via Cancel
            // navigation). Reload so server-side fields (UpdatedAt, etc.) are fresh.
            _loadedId = Id!.Value;
            _appliedInitialMode = InitialMode;
            _mode = ResolveInitialMode();
            await LoadAsync();
        }
        else if (IsCreateMode && _loadedId != Guid.Empty)
        {
            _loadedId = Guid.Empty;
            _mode = FormMode.Create;
            ResetFields();
            await SetIsDirty(false);
            ClearErrors();
            await SetGlobalError(null);
            _loading = false;
        }
        else if (IsCreateMode && _loading)
        {
            _mode = FormMode.Create;
            ResetFields();
            ClearErrors();
            _loading = false;
        }

        RebuildShortcuts();
    }

    private FormMode ResolveInitialMode()
    {
        var desired = InitialMode ?? (EnableViewMode ? FormMode.View : FormMode.Edit);
        return desired == FormMode.Edit && !CanEdit ? FormMode.View : desired;
    }

    private void HandlePermissionsChanged()
    {
        // Permissions can arrive asynchronously (ClaimsPermissionService loads claims after
        // construction) or be revoked at runtime. Re-resolve mode and shortcuts so the UI
        // matches the current authorization state.
        _ = InvokeAsync(async () =>
        {
            if (!IsCreateMode)
            {
                if (_mode == FormMode.Edit && !CanEdit && EnableViewMode)
                {
                    // Permissions revoked while editing: drop back to View and discard any
                    // unsaved local edits so the read-only inputs do not display stale state.
                    _mode = FormMode.View;
                    await LoadAsync();
                }
                else if (_mode == FormMode.View && InitialMode == FormMode.Edit && CanEdit)
                {
                    // Initial mode was downgraded; promote back to Edit now that we have permission.
                    _mode = FormMode.Edit;
                }
            }

            RebuildShortcuts();
            StateHasChanged();
        });
    }

    private void RebuildShortcuts()
    {
        var bindings = new List<ScopeBinding>();

        if (_mode == FormMode.View)
        {
            if (CanEdit)
            {
                bindings.Add(new("switch-to-edit", "m", ctrl: true, handler: SwitchToEditAsync));
            }
        }
        else
        {
            bindings.Add(new("save", "s", ctrl: true, handler: SaveAsync));
            bindings.Add(new("cancel", "Escape", handler: CancelAsync));

            if (CreateUrl is not null)
            {
                bindings.Add(new("save-and-new", "S", ctrl: true, shift: true, handler: SaveAndNewAsync));
            }
        }

        if (AdditionalShortcuts is not null)
        {
            bindings.AddRange(AdditionalShortcuts);
        }

        _shortcuts = bindings;
    }

    private async Task LoadAsync()
    {
        _loading = true;
        await SetIsDirty(false);
        ClearErrors();
        await SetGlobalError(null);

        try
        {
            var entity = await LoadEntity(Id!.Value);
            if (entity is null)
            {
                ToastService.Show("Élément introuvable.", Severity.Error);
                Nav.NavigateTo(ListUrl);
                return;
            }

            MapEntity(entity);
        }
        catch (Exception ex)
        {
            ToastService.Show($"Erreur : {ex.Message}", Severity.Error);
            Nav.NavigateTo(ListUrl);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task<Guid?> PerformSaveAsync()
    {
        ClearErrors();
        await SetGlobalError(null);

        if (!Validate())
        {
            return null;
        }

        _saving = true;
        try
        {
            Guid resultId;
            if (IsCreateMode)
            {
                if (CreateEntity is null)
                {
                    return null;
                }

                resultId = await CreateEntity();
            }
            else
            {
                if (UpdateEntity is null)
                {
                    return null;
                }

                await UpdateEntity();
                resultId = Id!.Value;
            }

            await SetIsDirty(false);
            return resultId;
        }
        catch (Exception ex)
        {
            if (MapDomainError is not null)
            {
                MapDomainError(ex.Message);
            }
            else
            {
                await SetGlobalError(ex.Message);
            }

            return null;
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task SaveAsync()
    {
        if (_mode == FormMode.View)
        {
            return;
        }

        if (!IsCreateMode && !CanEdit)
        {
            return;
        }

        var wasCreate = IsCreateMode;
        var id = await PerformSaveAsync();
        if (id is null)
        {
            return;
        }

        ToastService.Show(wasCreate ? "Élément créé." : "Élément modifié.", Severity.Success);

        var targetUrl = DetailUrl is not null ? DetailUrl(id.Value) : $"{ListUrl}/{id}";
        var alreadyOnTarget = !wasCreate && Nav.Uri.EndsWith($"/{id}", StringComparison.OrdinalIgnoreCase);

        if (!wasCreate && EnableViewMode && alreadyOnTarget)
        {
            // Update on the canonical detail URL: drop back to View in place,
            // since NavigateTo to the same URL would not re-mount the component
            // and would leave _mode = Edit.
            await LoadAsync();
            _mode = FormMode.View;
            _appliedInitialMode = FormMode.View;
            RebuildShortcuts();
            StateHasChanged();
            return;
        }

        Nav.NavigateTo(targetUrl);
    }

    private async Task SaveAndNewAsync()
    {
        if (_mode == FormMode.View || CreateUrl is null)
        {
            return;
        }

        if (!IsCreateMode && !CanEdit)
        {
            return;
        }

        var id = await PerformSaveAsync();
        if (id is null)
        {
            return;
        }

        ToastService.Show("Élément enregistré. Nouveau formulaire.", Severity.Success);

        if (IsCreateMode)
        {
            ResetFields();
            await SetIsDirty(false);
            ClearErrors();
            await SetGlobalError(null);
            _mode = FormMode.Create;
        }
        else
        {
            Nav.NavigateTo(CreateUrl);
        }
    }

    private async Task SwitchToEditAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        _mode = FormMode.Edit;
        RebuildShortcuts();
        await InvokeAsync(StateHasChanged);
    }

    private async Task CancelAsync()
    {
        if (_mode == FormMode.Edit && EnableViewMode && !IsCreateMode)
        {
            var isOnEditRoute = Nav.Uri.EndsWith("/edit", StringComparison.OrdinalIgnoreCase);
            if (isOnEditRoute)
            {
                // Deep-link /edit: navigate to the view URL so the URL reflects the state.
                Nav.NavigateTo(CancelUrl);
                return;
            }

            await LoadAsync();
            _mode = FormMode.View;
            RebuildShortcuts();
            return;
        }

        Nav.NavigateTo(CancelUrl);
    }

    private async Task HandleNavLock(LocationChangingContext context)
    {
        if (!IsDirty)
        {
            return;
        }

        var confirmed = await JS.InvokeAsync<bool>(
            "confirm",
            "Vous avez des modifications non enregistrées. Quitter quand même ?");

        if (!confirmed)
        {
            context.PreventNavigation();
        }
    }

    private async Task SetIsDirty(bool value)
    {
        if (IsDirty != value)
        {
            IsDirty = value;
            await IsDirtyChanged.InvokeAsync(value);
        }
    }

    private async Task SetGlobalError(string? value)
    {
        if (GlobalError != value)
        {
            GlobalError = value;
            await GlobalErrorChanged.InvokeAsync(value);
        }
    }
}
