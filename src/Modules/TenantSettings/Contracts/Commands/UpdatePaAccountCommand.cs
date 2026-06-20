namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Met à jour un compte Plateforme Agréée du tenant courant (F12-A §4). Chaque secret suit la même
/// sémantique de rotation : <c>null</c>/vide = secret inchangé ; une valeur non vide le remplace (chiffré
/// par le handler, jamais persisté en clair — CLAUDE.md n°10). <see cref="ApiKey"/> pour l'auth ApiKey ;
/// <see cref="ClientId"/> + <see cref="ClientSecret"/> pour l'auth OAuth2ClientCredentials.
/// <see cref="Environment"/> ∈ { « Staging », « Production » }.
/// </summary>
public record UpdatePaAccountCommand : ICommand
{
    public required Guid PaAccountId { get; init; }

    public required string Environment { get; init; }

    public string? AccountIdentifiers { get; init; }

    public string? ApiKey { get; init; }

    /// <summary>« client_id » OAuth2 EN CLAIR. null/vide = inchangé ; non vide = rotation (chiffré par le handler).</summary>
    public string? ClientId { get; init; }

    /// <summary>« client_secret » OAuth2 EN CLAIR. null/vide = inchangé ; non vide = rotation (chiffré par le handler).</summary>
    public string? ClientSecret { get; init; }

    /// <summary>Mot de passe du compte TECHNIQUE EN CLAIR (auth OAuth2WithTechnicalAccount). null/vide = inchangé ; non vide = rotation (chiffré par le handler).</summary>
    public string? TechnicalPassword { get; init; }
}
