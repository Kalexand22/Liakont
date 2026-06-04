namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Ajoute un compte Plateforme Agréée au tenant courant (F12-A §4). Retourne l'id du compte.
/// <see cref="Environment"/> ∈ { « Staging », « Production » }. <see cref="ApiKey"/> est la clé
/// EN CLAIR transmise par l'appelant ; le handler la chiffre immédiatement (Data Protection) et ne
/// la persiste jamais en clair (CLAUDE.md n°10). <c>null</c>/vide = aucune clé (à compléter ensuite).
/// </summary>
public record AddPaAccountCommand : ICommand<Guid>
{
    public required string PluginType { get; init; }

    public required string Environment { get; init; }

    public string? AccountIdentifiers { get; init; }

    public string? ApiKey { get; init; }
}
