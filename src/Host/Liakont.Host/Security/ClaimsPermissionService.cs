namespace Liakont.Host.Security;

using Microsoft.AspNetCore.Components.Authorization;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Claims-based <see cref="IPermissionService"/> for real (non-Showcase) pages.
/// Reads "permission" claims from the current user's <see cref="AuthenticationState"/>.
/// Users in a super-admin role bypass all permission checks.
/// </summary>
internal sealed class ClaimsPermissionService : IPermissionService, IDisposable
{
    private const string PermissionClaimType = "permission";

    private readonly AuthenticationStateProvider _authStateProvider;
    private HashSet<string> _permissions = [];
    private bool _isSuperAdmin;

    public ClaimsPermissionService(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
        _authStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _ = InitializeAsync();
    }

    public event Action? OnPermissionsChanged;

    public bool HasPermission(string permission) => _isSuperAdmin || _permissions.Contains(permission);

    public void Dispose()
    {
        _authStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var state = await _authStateProvider.GetAuthenticationStateAsync();
            RefreshPermissions(state);
            OnPermissionsChanged?.Invoke();
        }
        catch
        {
            // Swallow — permission set remains empty (deny-all) until next auth state change.
        }
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        _ = HandleAuthStateChangedAsync(task);
    }

    private async Task HandleAuthStateChangedAsync(Task<AuthenticationState> task)
    {
        try
        {
            var state = await task;
            RefreshPermissions(state);
            OnPermissionsChanged?.Invoke();
        }
        catch
        {
            // Swallow — permissions degrade to deny-all rather than staying stale.
            _permissions = [];
        }
    }

    private void RefreshPermissions(AuthenticationState state)
    {
        _permissions = state.User.Claims
            .Where(c => c.Type == PermissionClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _isSuperAdmin = SuperAdminRoles.IsSuperAdmin(state.User);
    }
}
