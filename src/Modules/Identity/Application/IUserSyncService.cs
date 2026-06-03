namespace Stratum.Modules.Identity.Application;

using System.Security.Claims;

/// <summary>
/// Synchronizes OIDC users into the Stratum Identity module.
/// On first login: creates a User domain entity from OIDC claims.
/// On subsequent logins: updates mutable fields (email, display name) if changed.
/// </summary>
public interface IUserSyncService
{
    /// <summary>
    /// Synchronizes the authenticated OIDC user into the local User store.
    /// Returns the Stratum UserId (existing or newly created).
    /// </summary>
    Task<Guid> SyncFromOidcClaimsAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}
