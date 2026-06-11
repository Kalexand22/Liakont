namespace Liakont.Host.Components;

using System;

/// <summary>
/// Restitution FRANÇAISE de l'acteur d'un événement de la piste d'audit d'un document (item FIX305, onglet
/// Historique) : affiche le NOM d'affichage persisté avec l'événement quand il existe, sinon retombe
/// proprement sur l'identité technique (GUID) avec une mention neutre (« compte 30da7398… ») pour les
/// événements antérieurs sans nom. Fonction TOTALE et PURE d'affichage (aucune règle métier interprétée,
/// CLAUDE.md n°2/19 ; aucune résolution différée — le nom vient de l'événement, jamais d'un annuaire) :
/// jamais d'exception, jamais d'acteur masqué. Le GUID complet reste disponible en infobulle (détail
/// technique) — voir <see cref="Tooltip"/>.
/// </summary>
public static class DocumentActorDisplay
{
    /// <summary>
    /// Libellé lisible de l'acteur : le <paramref name="operatorName"/> (nom persisté à l'événement) s'il
    /// existe ; sinon, pour un <paramref name="operatorIdentity"/> qui est un GUID, la mention neutre
    /// « compte {préfixe}… » (événement antérieur à FIX305) ; sinon l'identité brute telle quelle (ex.
    /// « system », ou une identité déjà lisible). <c>null</c> si aucun acteur (événement système).
    /// </summary>
    public static string? Label(string? operatorName, string? operatorIdentity)
    {
        if (!string.IsNullOrWhiteSpace(operatorName))
        {
            return operatorName.Trim();
        }

        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            return null;
        }

        var identity = operatorIdentity.Trim();

        // Repli pour les événements antérieurs sans nom persisté : un GUID n'est pas lisible pour un
        // vérificateur — on le présente comme un « compte » technique abrégé, le GUID complet restant en
        // infobulle. Une identité non-GUID (déjà lisible : nom legacy, « system ») est affichée telle quelle.
        return Guid.TryParse(identity, out _) ? $"compte {Shorten(identity)}" : identity;
    }

    /// <summary>
    /// Détail technique de l'acteur destiné à l'infobulle (<c>title</c>) : l'identité brute (GUID) quand
    /// elle est présente, pour qu'un vérificateur puisse toujours remonter à l'identifiant stable.
    /// <c>null</c> si aucune identité.
    /// </summary>
    public static string? Tooltip(string? operatorIdentity)
        => string.IsNullOrWhiteSpace(operatorIdentity) ? null : operatorIdentity.Trim();

    /// <summary>Abrège un identifiant technique (8 premiers caractères + ellipse) pour la mention de repli.</summary>
    private static string Shorten(string identity)
        => identity.Length <= 8 ? identity : identity[..8] + "…";
}
