namespace Liakont.Host.Startup;

internal sealed class AdminSeedOptions
{
    public string Username { get; init; } = "admin";

    /// <summary>Keycloak subject ID for the admin user. If empty, admin seeding is skipped.</summary>
    public string ExternalId { get; init; } = string.Empty;

    public string Email { get; init; } = "admin@stratum.local";

    public string DisplayName { get; init; } = "System Administrator";
}
