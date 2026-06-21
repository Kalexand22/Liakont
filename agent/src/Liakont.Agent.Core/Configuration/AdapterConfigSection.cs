namespace Liakont.Agent.Core.Configuration;

using System;
using System.Collections.Generic;

/// <summary>
/// Configuration SPÉCIFIQUE d'un adaptateur source (section <c>adapterConfig.&lt;nom&gt;</c> de
/// <c>agent.json</c>, ADR-0031). Le chargeur générique ne connaît pas les champs d'un adaptateur
/// donné : il transporte une section nommée de paires clé→valeur (chaînes). C'est la FABRIQUE de
/// chaque adaptateur qui lit et valide SA section (présence de l'émetteur, nature d'opération…),
/// avec des messages opérateur français (CLAUDE.md n°12). Aucun secret ici : la clé API et la
/// chaîne ODBC restent chiffrées dans <see cref="AgentConfig"/> / <see cref="ExtractionConfig"/>.
/// </summary>
public sealed class AdapterConfigSection
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public AdapterConfigSection(string adapterName, IReadOnlyDictionary<string, string> values)
    {
        AdapterName = adapterName ?? throw new ArgumentNullException(nameof(adapterName));
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>Nom de l'adaptateur auquel cette section appartient (utilisé dans les messages d'erreur).</summary>
    public string AdapterName { get; }

    /// <summary>Section VIDE (aucune configuration par-adaptateur fournie pour ce nom).</summary>
    public static AdapterConfigSection Empty(string adapterName) =>
        new AdapterConfigSection(adapterName, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Valeur d'un paramètre (clés INSENSIBLES à la casse), ou <c>null</c> si absent ou vide. Ne lève
    /// jamais : c'est à la fabrique de décider si l'absence est tolérable.
    /// </summary>
    public string? GetOptional(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return _values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    /// <summary>
    /// Valeur OBLIGATOIRE d'un paramètre. Lève <see cref="AgentConfigException"/> (message français
    /// nommant <c>adapterConfig.&lt;nom&gt;.&lt;clé&gt;</c>) si absente ou vide — bloquer plutôt que
    /// démarrer un adaptateur mal configuré (CLAUDE.md n°3).
    /// </summary>
    public string GetRequired(string key)
    {
        string? value = GetOptional(key);
        if (value is null)
        {
            throw new AgentConfigException(
                $"Le paramètre « adapterConfig.{AdapterName}.{key} » est absent. " +
                $"Renseignez-le dans agent.json pour l'adaptateur « {AdapterName} ».");
        }

        return value;
    }
}
