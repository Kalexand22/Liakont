namespace Liakont.Host.Components.Settings;

using System;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stratum.Modules.Identity.Application.Preferences;

/// <summary>
/// Helpers partagés des préférences utilisateur de la console (RBF08).
///
/// <para>Résolution de l'identifiant utilisateur (commun au panneau et à l'hydrateur de shell) et
/// gestion de la taille de page de grille.</para>
///
/// <para>La taille de page de grille est portée EN BASE (RBF08, décision « la préférence suit
/// l'utilisateur quel que soit le navigateur ») via le point d'extension <c>ExtensionsJson</c> du
/// modèle vendored <see cref="UserPreferences"/> — le modèle socle n'est pas modifié, on n'ajoute
/// ni colonne ni migration. Avant RBF08 elle vivait uniquement en localStorage (GUX06), donc perdue
/// d'un navigateur à l'autre.</para>
/// </summary>
internal static class UserPreferencesSupport
{
    /// <summary>Clé de la taille de page de grille dans <c>ExtensionsJson</c>.</summary>
    public const string GridPageSizeKey = "gridPageSize";

    /// <summary>Taille de page par défaut (cohérente avec le défaut JS <c>stratumUI.getGridPageSize</c>).</summary>
    public const int DefaultGridPageSize = 25;

    /// <summary>Valeurs autorisées (cohérentes avec le contrat JS <c>stratumUI.setGridPageSize</c>).</summary>
    public static readonly int[] AllowedGridPageSizes = [10, 25, 50, 100];

    /// <summary>
    /// Résout l'identifiant Stratum de l'utilisateur depuis ses claims, ou <see cref="Guid.Empty"/>
    /// si l'utilisateur n'est pas authentifié / non résoluble.
    /// </summary>
    public static Guid ResolveUserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return Guid.Empty;
        }

        var stratumUserId = user.FindFirst("stratum_user_id")?.Value;
        var nameIdentifier = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        var raw = stratumUserId ?? nameIdentifier;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Lit la taille de page de grille stockée dans <c>ExtensionsJson</c>, ou
    /// <see cref="DefaultGridPageSize"/> si absente / invalide / JSON illisible.
    /// </summary>
    public static int GetGridPageSize(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        try
        {
            var node = JsonNode.Parse(preferences.ExtensionsJson ?? "{}");
            if (node is JsonObject obj
                && obj.TryGetPropertyValue(GridPageSizeKey, out var value)
                && value is not null
                && value.GetValueKind() == JsonValueKind.Number
                && value.AsValue().TryGetValue<int>(out var size)
                && Array.IndexOf(AllowedGridPageSizes, size) >= 0)
            {
                return size;
            }
        }
        catch (JsonException)
        {
            // ExtensionsJson illisible : on retombe sur le défaut plutôt que de faire échouer le rendu.
        }

        return DefaultGridPageSize;
    }

    /// <summary>
    /// Indique si <c>ExtensionsJson</c> porte une taille de page de grille explicite et valide
    /// (utilisé par l'hydrateur pour n'écrire dans la couche client que lorsque la base a une valeur).
    /// </summary>
    public static bool HasExplicitGridPageSize(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        try
        {
            var node = JsonNode.Parse(preferences.ExtensionsJson ?? "{}");
            return node is JsonObject obj
                && obj.TryGetPropertyValue(GridPageSizeKey, out var value)
                && value is not null
                && value.GetValueKind() == JsonValueKind.Number
                && value.AsValue().TryGetValue<int>(out var size)
                && Array.IndexOf(AllowedGridPageSizes, size) >= 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Retourne une copie des préférences avec la taille de page de grille fixée dans
    /// <c>ExtensionsJson</c>, en PRÉSERVANT les autres clés d'extension. Lève
    /// <see cref="ArgumentOutOfRangeException"/> si la taille n'est pas autorisée.
    /// </summary>
    public static UserPreferences WithGridPageSize(UserPreferences preferences, int size)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (Array.IndexOf(AllowedGridPageSizes, size) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Taille de page de grille non autorisée.");
        }

        JsonObject obj;
        try
        {
            obj = JsonNode.Parse(preferences.ExtensionsJson ?? "{}") as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            // ExtensionsJson corrompu : on repart d'un objet vide plutôt que de propager l'erreur.
            obj = new JsonObject();
        }

        obj[GridPageSizeKey] = size;
        return preferences with { ExtensionsJson = obj.ToJsonString() };
    }
}
