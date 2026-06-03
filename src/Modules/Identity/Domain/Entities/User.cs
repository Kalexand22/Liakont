namespace Stratum.Modules.Identity.Domain.Entities;

using Stratum.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// User aggregate root for Identity module.
/// Domain-level invariants enforced here:
///   INV-IDENTITY-003: no duplicate role on a user (AssignRole)
///   INV-IDENTITY-004: system roles immutable (Role entity)
///   INV-IDENTITY-007: username format (Username value object)
/// Authentication is delegated to Keycloak (external IdP).
/// </summary>
public sealed class User
{
    private readonly List<Role> _roles = [];

    private User()
    {
    }

    public Guid Id { get; private set; }

    public Username Username { get; private set; } = null!;

    public EmailAddress Email { get; private set; } = null!;

    public string? DisplayName { get; private set; }

    /// <summary>Legacy password hash column — retained for DB compatibility. Empty for OIDC users.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>External identity provider subject ID (e.g. Keycloak sub claim).</summary>
    public string? ExternalId { get; private set; }

    /// <summary>Optional link to a Party entity.</summary>
    public Guid? PartyId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public IReadOnlyList<Role> Roles => _roles.AsReadOnly();

    /// <summary>
    /// Creates a new User provisioned from an OIDC provider (no password required).
    /// Used by UserSyncService on first login via Keycloak.
    /// </summary>
    public static User CreateFromOidc(
        string externalId,
        string username,
        string email,
        string? displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        return new User
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Username = Username.From(username),
            Email = EmailAddress.From(email),
            DisplayName = displayName,
            PasswordHash = string.Empty,
            PartyId = null,
            IsActive = true,
            LastLoginAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static User Reconstitute(
        Guid id,
        string username,
        string email,
        string? displayName,
        string passwordHash,
        Guid? partyId,
        bool isActive,
        DateTimeOffset? lastLoginAt,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt,
        IEnumerable<Role> roles,
        string? externalId = null)
    {
        var user = new User
        {
            Id = id,
            ExternalId = externalId,
            Username = Username.From(username),
            Email = EmailAddress.From(email),
            DisplayName = displayName,
            PasswordHash = passwordHash,
            PartyId = partyId,
            IsActive = isActive,
            LastLoginAt = lastLoginAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };

        user._roles.AddRange(roles);
        return user;
    }

    /// <summary>
    /// Updates mutable fields from OIDC claims on subsequent logins.
    /// Returns true if any field actually changed.
    /// </summary>
    public bool UpdateFromOidc(string email, string? displayName)
    {
        var newEmail = EmailAddress.From(email);
        bool changed = false;

        if (Email.Value != newEmail.Value)
        {
            Email = newEmail;
            changed = true;
        }

        if (DisplayName != displayName)
        {
            DisplayName = displayName;
            changed = true;
        }

        // Always record login timestamp regardless of data changes
        LastLoginAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;

        return changed;
    }

    /// <summary>
    /// Deactivates this user.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Assigns a role to this user. INV-IDENTITY-003: no duplicate roles.
    /// </summary>
    public void AssignRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (_roles.Any(r => r.Id == role.Id))
        {
            throw new InvalidOperationException(
                $"User '{Id}' already has role '{role.Name}'. (INV-IDENTITY-003)");
        }

        _roles.Add(role);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Revokes a role from this user.
    /// </summary>
    public void RevokeRole(Guid roleId)
    {
        var role = _roles.FirstOrDefault(r => r.Id == roleId)
            ?? throw new InvalidOperationException($"User '{Id}' does not have role '{roleId}'.");

        _roles.Remove(role);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Links this user to an external identity provider by setting the ExternalId.
    /// Used to back-fill pre-OIDC users so UserSyncService can match them on login.
    /// </summary>
    public void LinkExternalId(string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        if (!string.IsNullOrWhiteSpace(ExternalId))
        {
            throw new InvalidOperationException(
                $"User '{Id}' already has ExternalId '{ExternalId}'. Cannot overwrite.");
        }

        ExternalId = externalId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Replaces the external identity provider ID. Used when the IdP is reset
    /// (e.g., Keycloak dev rebuild) and the same user gets a new subject ID.
    /// </summary>
    public void RelinkExternalId(string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ExternalId = externalId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records a successful login timestamp.
    /// </summary>
    public void RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
