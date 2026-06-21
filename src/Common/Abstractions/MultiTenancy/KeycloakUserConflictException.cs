// Liakont addition (OPS03 §4.25): provisioning utilisateur Keycloak - not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// A user creation was rejected by Keycloak with a conflict (HTTP 409) — the username OR the
/// email already exists in the realm. Typed so callers can map it to a clean operator-facing
/// refusal instead of an opaque 500 (a username pre-check cannot cover realm-unique emails).
/// </summary>
public sealed class KeycloakUserConflictException : InvalidOperationException
{
    public KeycloakUserConflictException(string message)
        : base(message)
    {
    }

    public KeycloakUserConflictException()
    {
    }

    public KeycloakUserConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
