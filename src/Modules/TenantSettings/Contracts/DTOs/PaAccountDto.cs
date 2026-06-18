namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Compte Plateforme Agréée en lecture (F12-A §4).
/// </summary>
/// <remarks>
/// <strong>INV-TENANTSETTINGS-003 :</strong> ce DTO n'expose JAMAIS la clé API (ni en clair ni
/// chiffrée). Seul <see cref="HasApiKey"/> indique si une clé a été saisie (CLAUDE.md n°10).
/// </remarks>
public record PaAccountDto
{
    public required Guid Id { get; init; }

    public required Guid CompanyId { get; init; }

    public required string PluginType { get; init; }

    public required string Environment { get; init; }

    public required string AccountIdentifiers { get; init; }

    /// <summary>Indique qu'une clé API a été saisie (chiffrée en base) — jamais la clé elle-même.</summary>
    public required bool HasApiKey { get; init; }

    /// <summary>Indique qu'un « client_id » OAuth2 a été saisi (chiffré en base) — jamais la valeur elle-même.</summary>
    public bool HasClientId { get; init; }

    /// <summary>Indique qu'un « client_secret » OAuth2 a été saisi (chiffré en base) — jamais la valeur elle-même.</summary>
    public bool HasClientSecret { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
