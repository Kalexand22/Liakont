namespace Liakont.Host.Security;

/// <summary>
/// Configuration for an additional Keycloak realm.
/// </summary>
internal sealed class KeycloakRealmConfig
{
    public string Authority { get; init; } = string.Empty;

    public string ClientId { get; init; } = "stratum";

    public string ClientSecret { get; init; } = string.Empty;
}
