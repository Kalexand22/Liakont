namespace Stratum.Modules.Identity.Infrastructure;

using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Domain.Repositories;

internal sealed partial class UserSyncService : IUserSyncService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<UserSyncService> _logger;
    private readonly UserSyncOptions _options;

    public UserSyncService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILogger<UserSyncService> logger,
        IOptions<UserSyncOptions> options)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<Guid> SyncFromOidcClaimsAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("OIDC principal has no sub/NameIdentifier claim.");

        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email")
                    ?? throw new InvalidOperationException("OIDC principal has no email claim.");

        var username = principal.FindFirstValue("preferred_username")
                      ?? SanitizeUsername(email);
        var displayName = principal.FindFirstValue("display_name")
                          ?? principal.FindFirstValue("name");

        var existingUser = await _userRepository.GetByExternalId(sub, ct);

        if (existingUser is not null)
        {
            return await HandleExistingUser(existingUser, email, displayName, ct);
        }

        return await CreateNewUser(sub, username, email, displayName, ct);
    }

    private async Task<Guid> HandleExistingUser(
        User user,
        string email,
        string? displayName,
        CancellationToken ct)
    {
        bool dataChanged = user.UpdateFromOidc(email, displayName);

        // Always persist to record LastLoginAt, even if email/displayName unchanged
        await _userRepository.Update(user, ct);

        if (dataChanged)
        {
            LogUserUpdated(_logger, user.Id, user.ExternalId);
        }
        else
        {
            LogUserUnchanged(_logger, user.Id);
        }

        return user.Id;
    }

    private async Task<Guid> CreateNewUser(
        string externalId,
        string username,
        string email,
        string? displayName,
        CancellationToken ct)
    {
        // Disambiguate username if it already exists locally
        var resolvedUsername = await ResolveUniqueUsernameAsync(username, ct);

        var user = User.CreateFromOidc(externalId, resolvedUsername, email, displayName);

        var defaultRole = await _roleRepository.GetByName(_options.DefaultRoleName, ct);
        if (defaultRole is not null)
        {
            user.AssignRole(defaultRole);
        }
        else
        {
            LogDefaultRoleNotFound(_logger, _options.DefaultRoleName, user.Id);
        }

        try
        {
            await _userRepository.Insert(user, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Race condition: another request created the same sub concurrently
            var raceWinner = await _userRepository.GetByExternalId(externalId, ct);
            if (raceWinner is not null)
            {
                return await HandleExistingUser(raceWinner, email, displayName, ct);
            }

            // User exists with same email — link or re-link to the current Keycloak sub.
            // Covers both legacy users (no external_id) and Keycloak resets (stale external_id).
            if (ex.ConstraintName == "uq_users_email")
            {
                var existingByEmail = await _userRepository.GetByEmail(email, ct);
                if (existingByEmail is not null)
                {
                    existingByEmail.RelinkExternalId(externalId);
                    return await HandleExistingUser(existingByEmail, email, displayName, ct);
                }
            }

            throw new InvalidOperationException(
                $"Failed to create OIDC user: unique constraint violation (constraint: {ex.ConstraintName})", ex);
        }

        LogUserCreated(_logger, user.Id, externalId, resolvedUsername);

        return user.Id;
    }

    private async Task<string> ResolveUniqueUsernameAsync(string baseUsername, CancellationToken ct)
    {
        var existing = await _userRepository.GetByUsername(baseUsername, ct);
        if (existing is null)
        {
            return baseUsername;
        }

        // Append incrementing suffix until unique
        for (int i = 1; i <= 100; i++)
        {
            var candidate = $"{baseUsername}_{i}";
            if (candidate.Length > 50)
            {
                candidate = $"{baseUsername[..Math.Min(baseUsername.Length, 47)]}_{i}";
            }

            var check = await _userRepository.GetByUsername(candidate, ct);
            if (check is null)
            {
                return candidate;
            }
        }

        // Extremely unlikely — fall back to GUID-based username
        return $"oidc_{Guid.NewGuid():N}"[..50];
    }

#pragma warning disable SA1204 // Static members should appear before non-static members (LoggerMessage source generators must be at class bottom)

    /// <summary>
    /// Extracts the local part of an email and replaces non-alphanumeric chars with underscores
    /// to satisfy the Username value object (3-50 chars, alphanumeric + underscores).
    /// </summary>
    private static string SanitizeUsername(string email)
    {
        var localPart = email.Contains('@', StringComparison.Ordinal)
            ? email[..email.IndexOf('@', StringComparison.Ordinal)]
            : email;

        var sanitized = new string(localPart.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        return sanitized.Length >= 3
            ? sanitized[..Math.Min(sanitized.Length, 50)]
            : sanitized.PadRight(3, '_');
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated OIDC user {UserId} (ExternalId={ExternalId})")]
    private static partial void LogUserUpdated(ILogger logger, Guid userId, string? externalId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "OIDC user {UserId} unchanged, skipping update")]
    private static partial void LogUserUnchanged(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Default role '{RoleName}' not found. User {UserId} created without roles")]
    private static partial void LogDefaultRoleNotFound(ILogger logger, string roleName, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created new OIDC user {UserId} (ExternalId={ExternalId}, Username={Username})")]
    private static partial void LogUserCreated(ILogger logger, Guid userId, string externalId, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "Linked legacy user {UserId} to ExternalId={ExternalId} (matched by email={Email})")]
    private static partial void LogUserLinked(ILogger logger, Guid userId, string externalId, string email);
#pragma warning restore SA1204
}
