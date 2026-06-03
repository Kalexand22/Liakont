namespace Stratum.Common.Abstractions.Security;

/// <summary>
/// Resolves whether the current user holds a given permission string.
/// Register a concrete implementation in the host (e.g. <c>FakePermissionService</c>
/// for Showcase, or a real claims-based service for production).
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Raised when permissions change (e.g. role switch).
    /// Callers are responsible for dispatching to the correct synchronisation context
    /// (e.g. <c>InvokeAsync(StateHasChanged)</c> on Blazor components).
    /// </summary>
    event Action? OnPermissionsChanged;

    /// <summary>Returns true when the current user has <paramref name="permission"/>.</summary>
    bool HasPermission(string permission);
}
