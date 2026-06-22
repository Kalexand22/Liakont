// Liakont addition (OPS03 §4.25): provisioning utilisateur Keycloak - not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Specification of a user to create in an existing Keycloak realm
/// (see <see cref="IKeycloakUserProvisioner.CreateUserAsync"/>).
/// </summary>
public sealed record KeycloakUserSpec
{
    /// <summary>Unique username in the realm.</summary>
    public required string Username { get; init; }

    /// <summary>Email address (realms enforce unique emails).</summary>
    public required string Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>Marks the email as verified (no verification round-trip for operator-created users).</summary>
    public bool EmailVerified { get; init; }

    /// <summary>Required actions at next login — e.g. <c>UPDATE_PASSWORD</c>.</summary>
    public IReadOnlyList<string> RequiredActions { get; init; } = [];

    /// <summary>Initial user attributes (more can be set later via SetUserAttributesAsync).</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>();
}
