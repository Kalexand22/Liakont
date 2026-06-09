namespace Stratum.Common.UI.Models;

/// <summary>
/// Configuration for a bulk action in <see cref="Components.DeclaredListPage{TItem}"/>.
/// </summary>
/// <param name="Id">Unique action identifier.</param>
/// <param name="Label">Button label shown in the bulk action bar.</param>
/// <param name="Icon">Optional Bootstrap icon class (e.g. "bi-pause").</param>
/// <param name="Permission">Permission string for PermissionGate (null = no gate).</param>
/// <param name="PermissionDisabledReason">Tooltip when permission is insufficient.</param>
/// <param name="Execute">Callback receiving the filtered list of selected items.</param>
/// <param name="ItemFilter">Optional predicate to filter selected items before execution (e.g. only active items).</param>
/// <param name="RequiresConfirmation">Whether to show a confirmation dialog before executing.</param>
/// <param name="SuppressSuccessToast">
/// When true, the generic "{N} élément(s) traité(s)." success toast is NOT shown after <see cref="Execute"/>
/// returns. Use this for actions whose <see cref="Execute"/> does not COMPLETE the operation but defers it to an
/// explicit confirmation step (so an unconditional "done" toast would be misleading). Defaults to false
/// (unchanged behaviour for existing bulk actions).
/// </param>
public sealed record BulkActionConfig<TItem>(
    string Id,
    string Label,
    string? Icon = null,
    string? Permission = null,
    string? PermissionDisabledReason = null,
    Func<IReadOnlyList<TItem>, Task>? Execute = null,
    Func<TItem, bool>? ItemFilter = null,
    bool RequiresConfirmation = false,
    bool SuppressSuccessToast = false);
