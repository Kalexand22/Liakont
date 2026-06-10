namespace Liakont.Host.Startup;

/// <summary>
/// Options du seed de DÉVELOPPEMENT du tenant par défaut (section <c>DevTenantSeed</c>,
/// posée uniquement dans appsettings.Development.json). <see cref="TenantId"/> vide = seed désactivé.
/// </summary>
internal sealed class DevTenantSeedOptions
{
    /// <summary>Identifiant du tenant à amorcer (ex. <c>default</c>). Vide = pas de seed.</summary>
    public string TenantId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = "Tenant de développement";

    public string AdminEmail { get; init; } = "dev@liakont.local";

    /// <summary>Realm Keycloak déjà importé à rattacher au tenant (ex. <c>liakont-dev</c>).</summary>
    public string RealmName { get; init; } = string.Empty;

    /// <summary>Base de données du tenant (en dev : la base partagée, cf. TenantConnections).</summary>
    public string DatabaseName { get; init; } = string.Empty;

    /// <summary>Secret client du realm de dev (placeholder committé, dev local uniquement).</summary>
    public string? ClientSecret { get; init; }
}
