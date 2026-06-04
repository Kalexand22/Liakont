namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>Profil de tenant en lecture (F12-A §2). Le statut est exposé en chaîne (« Actif »/« Suspendu »).</summary>
public record TenantProfileDto
{
    public required Guid Id { get; init; }

    public required Guid CompanyId { get; init; }

    public required string Siren { get; init; }

    public required string RaisonSociale { get; init; }

    public required string Street { get; init; }

    public required string PostalCode { get; init; }

    public required string City { get; init; }

    public required string Country { get; init; }

    public string? ContactEmailAlerte { get; init; }

    public required string Statut { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
