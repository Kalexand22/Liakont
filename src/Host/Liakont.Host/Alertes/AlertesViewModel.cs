namespace Liakont.Host.Alertes;

using Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>
/// Modèle assemblé de la page « Paramétrage › Alertes » (FIX210, F12 §5) : le dispositif en lecture (règles
/// actives + gelées, seuils effectifs, e-mail opérateur) et la saisie éditable (seuils des règles actives,
/// contact d'alerte, activation). Jamais de secret ni d'adresse opérateur (CLAUDE.md n°10).
/// </summary>
public sealed record AlertesViewModel
{
    /// <summary>État du dispositif (règles, e-mail opérateur, cadence) en lecture.</summary>
    public required AlertDeviceStatusDto Device { get; init; }

    /// <summary>Valeurs éditables (pré-remplies aux seuils du tenant ou aux défauts F12 §5.2).</summary>
    public required AlertesFormModel Form { get; init; }

    /// <summary>
    /// <c>true</c> si le profil du tenant existe (le contact d'alerte se rattache au profil) : sinon le
    /// contact n'est pas éditable tant que le profil légal n'est pas créé.
    /// </summary>
    public required bool ProfileExists { get; init; }
}
