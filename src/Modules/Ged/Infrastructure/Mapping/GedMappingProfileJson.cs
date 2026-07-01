namespace Liakont.Modules.Ged.Infrastructure.Mapping;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Sérialisation JSON des règles d'un <c>GedMappingProfile</c> vers/depuis les colonnes <c>jsonb</c> de
/// <c>ged_catalog.ged_mapping_profiles</c> (F19 §4.5). Format INTERNE (lu/écrit par le même code) : ce n'est
/// PAS le sérialiseur canonique du hash fiscal — aucune contrainte de stabilité octet inter-runtime ici.
/// </summary>
internal static class GedMappingProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Sérialise une liste de règles vers un tableau JSON (jamais <see langword="null"/> — « [] » si vide).</summary>
    /// <typeparam name="T">Le type de règle.</typeparam>
    /// <param name="rules">Les règles.</param>
    /// <returns>Le JSON du tableau.</returns>
    public static string Serialize<T>(IReadOnlyList<T> rules) => JsonSerializer.Serialize(rules, Options);

    /// <summary>Désérialise un tableau JSON de règles ; rend une liste vide si le JSON est nul/vide.</summary>
    /// <typeparam name="T">Le type de règle.</typeparam>
    /// <param name="json">Le JSON du tableau.</param>
    /// <returns>Les règles.</returns>
    public static IReadOnlyList<T> Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<T>();
        }

        return JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>();
    }

    /// <summary>Sérialise un objet quelconque (instantané d'audit) avec les mêmes options.</summary>
    /// <param name="value">La valeur.</param>
    /// <returns>Le JSON.</returns>
    public static string SerializeValue(object value) => JsonSerializer.Serialize(value, Options);
}
