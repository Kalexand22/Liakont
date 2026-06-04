namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Met à jour un compte Plateforme Agréée du tenant courant (F12-A §4). <see cref="ApiKey"/> :
/// <c>null</c> = clé inchangée ; une valeur non vide remplace la clé (chiffrée par le handler,
/// jamais persistée en clair — CLAUDE.md n°10). <see cref="Environment"/> ∈ { « Staging »,
/// « Production » }.
/// </summary>
public record UpdatePaAccountCommand : ICommand
{
    public required Guid PaAccountId { get; init; }

    public required string Environment { get; init; }

    public string? AccountIdentifiers { get; init; }

    public string? ApiKey { get; init; }
}
