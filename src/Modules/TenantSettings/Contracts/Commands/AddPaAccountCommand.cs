namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Ajoute un compte Plateforme Agréée au tenant courant (F12-A §4). Retourne l'id du compte.
/// <see cref="Environment"/> ∈ { « Staging », « Production » }. Les secrets (<see cref="ApiKey"/> pour
/// l'auth ApiKey ; <see cref="ClientId"/> + <see cref="ClientSecret"/> pour l'auth OAuth2ClientCredentials)
/// sont transmis EN CLAIR par l'appelant ; le handler les chiffre immédiatement (Data Protection) et ne
/// les persiste jamais en clair (CLAUDE.md n°10). <c>null</c>/vide = secret non saisi (à compléter ensuite).
/// </summary>
public record AddPaAccountCommand : ICommand<Guid>
{
    public required string PluginType { get; init; }

    public required string Environment { get; init; }

    public string? AccountIdentifiers { get; init; }

    public string? ApiKey { get; init; }

    /// <summary>« client_id » OAuth2 EN CLAIR (auth OAuth2ClientCredentials). Chiffré par le handler ; null/vide = non saisi.</summary>
    public string? ClientId { get; init; }

    /// <summary>« client_secret » OAuth2 EN CLAIR (auth OAuth2ClientCredentials). Chiffré par le handler ; null/vide = non saisi.</summary>
    public string? ClientSecret { get; init; }
}
