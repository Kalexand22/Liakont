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

    /// <summary>
    /// Société (companyId) du tenant amorcé — DOIT correspondre au claim <c>company_id</c> que le realm
    /// de dev présente (codé en dur dans <c>deploy/docker/keycloak/realm-export.json</c>), sinon le profil
    /// seedé et l'utilisateur connecté ne partageraient pas la même clé de scoping. <see cref="Guid.Empty"/>
    /// (valeur par défaut) = amorçage du profil désactivé (seul le tenant système est enregistré).
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Dossier de seed du paramétrage (format <c>config/exemples/tenant-seed/</c>, profil FICTIF) importé
    /// dans la base du tenant après sa migration. Chemin relatif résolu par rapport au <c>ContentRoot</c>.
    /// Vide = pas d'import de profil. Aucun secret n'est importé (INV-TENANTSETTINGS-007).
    /// </summary>
    public string? SeedDirectoryPath { get; init; }
}
