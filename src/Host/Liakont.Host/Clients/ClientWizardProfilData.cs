namespace Liakont.Host.Clients;

/// <summary>Saisie de l'étape « Profil » de l'assistant « Nouveau client » (OPS03).</summary>
public sealed record ClientWizardProfilData
{
    /// <summary>Identifiant technique du tenant (slug : minuscules/chiffres/tirets, 1-63).</summary>
    public required string TenantId { get; init; }

    public required string RaisonSociale { get; init; }

    /// <summary>Email de contact (admin initial du registre + contact d'alerte du profil).</summary>
    public required string Email { get; init; }

    public required string Siren { get; init; }

    public required string Street { get; init; }

    public required string PostalCode { get; init; }

    public required string City { get; init; }

    public required string Country { get; init; }
}
