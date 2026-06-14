namespace Liakont.Host.Clients;

/// <summary>Saisie du profil client (étape 1 de l'assistant — chemin « sans seed »).</summary>
internal sealed record ClientProfileInput
{
    public required string Siren { get; init; }

    public required string RaisonSociale { get; init; }

    public required string Street { get; init; }

    public required string PostalCode { get; init; }

    public required string City { get; init; }

    public required string Country { get; init; }

    public string? ContactEmailAlerte { get; init; }
}
