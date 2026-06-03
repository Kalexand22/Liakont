namespace Stratum.Common.UI.Services.Filters;

using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Models;

/// <summary>
/// Default <see cref="IGridFilterAIService"/>: builds a structured prompt from a
/// column registry, calls the provider, parses the JSON, and applies DF-07 strict
/// validation (field in registry, operator in <see cref="FilterOperatorMap"/>,
/// value parseable for the column type).
/// </summary>
public sealed partial class GridFilterAIService : IGridFilterAIService
{
    private readonly IGridFilterAIProvider _provider;
    private readonly ILogger<GridFilterAIService> _logger;

    public GridFilterAIService(
        IGridFilterAIProvider provider,
        ILogger<GridFilterAIService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public bool IsAvailable => _provider.IsAvailable;

    public async Task<AIFilterProposal> GenerateAsync(
        IReadOnlyList<ColumnDefinition> columns,
        string userInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            return AIFilterProposal.Failed("Aucune colonne disponible pour filtrer.");
        }

        if (string.IsNullOrWhiteSpace(userInput))
        {
            return AIFilterProposal.Failed("Veuillez décrire le filtre souhaité.");
        }

        if (!_provider.IsAvailable)
        {
            return AIFilterProposal.Unavailable(
                "Assistant IA indisponible — utilisez le builder de filtres.");
        }

        var prompt = BuildPrompt(columns, userInput);

        GridFilterAIProviderResponse response;
        try
        {
            response = await _provider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogProviderException(ex);
            return AIFilterProposal.Failed(
                "L'assistant IA n'a pas pu traiter votre demande. Réessayez.");
        }

        if (!response.Success || string.IsNullOrWhiteSpace(response.ResponseJson))
        {
            LogNoUsablePayload(response.Error);
            return AIFilterProposal.Failed(
                response.Error ?? "L'assistant IA n'a renvoyé aucune proposition.");
        }

        return ParseAndValidate(columns, response.ResponseJson!);
    }

    /// <summary>
    /// Builds the structured prompt sent to the LLM. The prompt lists every
    /// allowed column, its type, the allowed operators, and any enum values.
    /// </summary>
    internal static string BuildPrompt(IReadOnlyList<ColumnDefinition> columns, string userInput)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tu es un assistant de filtrage pour une grille de données.");
        sb.AppendLine("Ta seule tâche est de traduire la demande de l'utilisateur en critères de filtre structurés.");
        sb.AppendLine();
        sb.AppendLine("## Colonnes disponibles");
        foreach (var col in columns)
        {
            sb.Append("- key=\"").Append(col.Key).Append('"');
            sb.Append(" | titre=\"").Append(col.Title).Append('"');
            sb.Append(" | type=").Append(col.DataType.ToString());
            if (col.AllowedValues is { Count: > 0 })
            {
                sb.Append(" | valeurs=[")
                  .Append(string.Join(", ", col.AllowedValues))
                  .Append(']');
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("## Opérateurs valides par type");
        foreach (var dataType in Enum.GetValues<ColumnDataType>())
        {
            var operators = FilterOperatorMap.GetOperators(dataType);
            if (operators.Count == 0)
            {
                continue;
            }

            sb.Append("- ").Append(dataType).Append(": ");
            sb.AppendLine(string.Join(", ", operators));
        }

        sb.AppendLine();
        sb.AppendLine("## Règles strictes");
        sb.AppendLine("1. Utilise UNIQUEMENT les `key` listées ci-dessus comme `field`.");
        sb.AppendLine("2. Utilise UNIQUEMENT les opérateurs autorisés pour le type du champ.");
        sb.AppendLine("3. Pour les enums, `value` doit être exactement une valeur de la liste.");
        sb.AppendLine("4. Pour les dates, utilise le format ISO 8601 (YYYY-MM-DD).");
        sb.AppendLine("5. Pour les booléens, utilise `true` ou `false`.");
        sb.AppendLine("6. Si un champ est ambigu ou non reconnu, ajoute-le dans `warnings` avec une suggestion.");
        sb.AppendLine("7. Si tu ne peux produire aucun critère valide, renvoie `criteria: []`.");
        sb.AppendLine();
        sb.AppendLine("## Format de réponse attendu (JSON strict)");
        sb.AppendLine("{");
        sb.AppendLine("  \"criteria\": [");
        sb.AppendLine("    { \"field\": \"<key>\", \"operator\": \"<op>\", \"value\": <valeur>, \"valueEnd\": <valeur|null> }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"warnings\": [\"message\"]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("## Demande utilisateur");
        sb.Append('"').Append(userInput.Trim()).Append('"');

        return sb.ToString();
    }

    /// <summary>
    /// Extracts the first JSON object from a response. Handles both:
    /// (1) raw JSON already, (2) OpenAI-compatible envelope where the JSON
    /// lives in <c>choices[0].message.content</c>, possibly wrapped in
    /// <c>```json ... ```</c> fences.
    /// </summary>
    internal static string? ExtractJsonObject(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var content = payload.Trim();

        if (content.StartsWith("{\"id\"", StringComparison.Ordinal)
            || content.Contains("\"choices\"", StringComparison.Ordinal))
        {
            try
            {
                using var envelope = JsonDocument.Parse(content);
                if (envelope.RootElement.TryGetProperty("choices", out var choices)
                    && choices.ValueKind == JsonValueKind.Array
                    && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var contentEl)
                    && contentEl.ValueKind == JsonValueKind.String)
                {
                    content = contentEl.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // Not an envelope — treat payload as raw content.
            }
        }

        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = content.IndexOf('\n');
            if (firstNewline > 0)
            {
                content = content[(firstNewline + 1)..];
            }

            if (content.EndsWith("```", StringComparison.Ordinal))
            {
                content = content[..^3];
            }

            content = content.Trim();
        }

        // Preferred: parse the trimmed content directly. If the LLM obeyed the
        // response_format=json_object directive we get clean JSON here.
        if (TryValidateJson(content))
        {
            return content;
        }

        // Fallback: the content has prose or stray braces around the object.
        // Scan for the first balanced object at depth 0 and return it.
        return ExtractBalancedJsonObject(content);
    }

    private static bool TryValidateJson(string candidate)
    {
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ExtractBalancedJsonObject(string content)
    {
        var start = content.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < content.Length; i++)
        {
            var c = content[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static FilterCriterion? TryBuildCriterion(
        JsonElement item,
        Dictionary<string, ColumnDefinition> byKey,
        IReadOnlyList<ColumnDefinition> columns,
        List<string> warnings)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var fieldName = TryGetStringProperty(item, "field", "key");
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            warnings.Add("Critère ignoré : champ absent.");
            return null;
        }

        if (!byKey.TryGetValue(fieldName, out var column))
        {
            var suggestion = SuggestNearestField(columns, fieldName);
            warnings.Add(suggestion is not null
                ? $"Champ « {fieldName} » inconnu. Suggestion : « {suggestion.Title} »."
                : $"Champ « {fieldName} » inconnu.");
            return null;
        }

        var operatorName = TryGetStringProperty(item, "operator", "op");
        if (string.IsNullOrWhiteSpace(operatorName)
            || !Enum.TryParse<FilterOperator>(operatorName, ignoreCase: true, out var op))
        {
            warnings.Add($"Opérateur « {operatorName ?? "?"} » invalide pour « {column.Title} ».");
            return null;
        }

        if (!FilterOperatorMap.IsOperatorValid(column.DataType, op))
        {
            warnings.Add(
                $"Opérateur « {op} » non autorisé sur le type {column.DataType} (« {column.Title} »).");
            return null;
        }

        object? value = null;
        object? valueEnd = null;

        if (op is not FilterOperator.IsNull and not FilterOperator.IsNotNull)
        {
            if (!TryParseValue(item, "value", column, out value, out var valueError))
            {
                warnings.Add($"Valeur invalide pour « {column.Title} » : {valueError}");
                return null;
            }

            if (op is FilterOperator.Between or FilterOperator.NotBetween)
            {
                if (!TryParseValue(item, "valueEnd", column, out valueEnd, out var endError))
                {
                    warnings.Add($"Borne supérieure invalide pour « {column.Title} » : {endError}");
                    return null;
                }
            }
        }

        return new FilterCriterion(column.Key, op, value, valueEnd);
    }

    private static bool TryParseValue(
        JsonElement parent,
        string propertyName,
        ColumnDefinition column,
        out object? value,
        out string error)
    {
        value = null;
        error = string.Empty;

        if (!parent.TryGetProperty(propertyName, out var element)
            || element.ValueKind == JsonValueKind.Null
            || element.ValueKind == JsonValueKind.Undefined)
        {
            error = "valeur manquante";
            return false;
        }

        switch (column.DataType)
        {
            case ColumnDataType.Text:
                if (element.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var entry in element.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.String)
                        {
                            list.Add(entry.GetString()!);
                        }
                    }

                    if (list.Count == 0)
                    {
                        error = "liste vide";
                        return false;
                    }

                    value = list.ToArray();
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    value = element.GetString();
                    return true;
                }

                error = "attendu: texte";
                return false;

            case ColumnDataType.Number:
            case ColumnDataType.Money:
                if (element.ValueKind == JsonValueKind.Array)
                {
                    var nums = new List<decimal>();
                    foreach (var entry in element.EnumerateArray())
                    {
                        if (TryReadDecimal(entry, out var n))
                        {
                            nums.Add(n);
                        }
                    }

                    if (nums.Count == 0)
                    {
                        error = "liste vide ou non numérique";
                        return false;
                    }

                    value = nums.Cast<object>().ToArray();
                    return true;
                }

                if (TryReadDecimal(element, out var num))
                {
                    value = num;
                    return true;
                }

                error = "attendu: nombre";
                return false;

            case ColumnDataType.Date:
                if (element.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(
                        element.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var date))
                {
                    value = date;
                    return true;
                }

                error = "attendu: date ISO 8601";
                return false;

            case ColumnDataType.Boolean:
                if (element.ValueKind == JsonValueKind.True)
                {
                    value = true;
                    return true;
                }

                if (element.ValueKind == JsonValueKind.False)
                {
                    value = false;
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String
                    && bool.TryParse(element.GetString(), out var b))
                {
                    value = b;
                    return true;
                }

                error = "attendu: booléen";
                return false;

            case ColumnDataType.Enum:
                if (column.AllowedValues is null || column.AllowedValues.Count == 0)
                {
                    error = "aucune valeur autorisée définie";
                    return false;
                }

                if (element.ValueKind == JsonValueKind.Array)
                {
                    var enumValues = new List<string>();
                    foreach (var entry in element.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.String
                            && TryMatchEnum(column.AllowedValues, entry.GetString(), out var match))
                        {
                            enumValues.Add(match);
                        }
                    }

                    if (enumValues.Count == 0)
                    {
                        error = $"aucune valeur ne correspond à {string.Join("/", column.AllowedValues)}";
                        return false;
                    }

                    value = enumValues.ToArray();
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String
                    && TryMatchEnum(column.AllowedValues, element.GetString(), out var enumMatch))
                {
                    value = enumMatch;
                    return true;
                }

                error = $"valeur absente de {string.Join("/", column.AllowedValues)}";
                return false;

            default:
                error = $"type {column.DataType} non pris en charge";
                return false;
        }
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out value);
        }

        if (element.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                element.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        value = 0m;
        return false;
    }

    private static bool TryMatchEnum(
        IReadOnlyList<string> allowed,
        string? candidate,
        out string match)
    {
        match = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var allowedValue in allowed)
        {
            if (string.Equals(allowedValue, candidate, StringComparison.OrdinalIgnoreCase))
            {
                match = allowedValue;
                return true;
            }
        }

        return false;
    }

    private static ColumnDefinition? SuggestNearestField(
        IReadOnlyList<ColumnDefinition> columns,
        string fieldName)
    {
        ColumnDefinition? best = null;
        var bestScore = int.MaxValue;
        foreach (var col in columns)
        {
            var keyDistance = LevenshteinDistance(col.Key, fieldName);
            var titleDistance = LevenshteinDistance(col.Title, fieldName);
            var score = Math.Min(keyDistance, titleDistance);
            if (score < bestScore)
            {
                bestScore = score;
                best = col;
            }
        }

        return bestScore <= Math.Max(3, fieldName.Length / 2) ? best : null;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var aLower = a.ToLowerInvariant();
        var bLower = b.ToLowerInvariant();

        var previous = new int[bLower.Length + 1];
        var current = new int[bLower.Length + 1];

        for (var j = 0; j <= bLower.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= aLower.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= bLower.Length; j++)
            {
                var cost = aLower[i - 1] == bLower[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[bLower.Length];
    }

    private static string? TryGetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }

        return null;
    }

    private AIFilterProposal ParseAndValidate(
        IReadOnlyList<ColumnDefinition> columns,
        string responseJson)
    {
        var content = ExtractJsonObject(responseJson);
        if (content is null)
        {
            return AIFilterProposal.Failed(
                "L'assistant IA a renvoyé une réponse non structurée.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            LogJsonParseFailed(ex, content);
            return AIFilterProposal.Failed(
                "L'assistant IA a renvoyé une réponse illisible.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var validated = new List<FilterCriterion>();
            var warnings = new List<string>();
            var byKey = columns.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("warnings", out var warningsEl)
                && warningsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in warningsEl.EnumerateArray())
                {
                    if (w.ValueKind == JsonValueKind.String)
                    {
                        var text = w.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            warnings.Add(text!);
                        }
                    }
                }
            }

            if (root.TryGetProperty("criteria", out var criteriaEl)
                && criteriaEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in criteriaEl.EnumerateArray())
                {
                    var criterion = TryBuildCriterion(item, byKey, columns, warnings);
                    if (criterion is not null)
                    {
                        validated.Add(criterion);
                    }
                }
            }

            var status = validated.Count > 0
                ? AIFilterProposalStatus.Success
                : AIFilterProposalStatus.Empty;

            return new AIFilterProposal(status, validated, warnings, ErrorMessage: null);
        }
    }

    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Warning,
        Message = "Grid filter AI provider threw an exception.")]
    private partial void LogProviderException(Exception ex);

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Debug,
        Message = "Grid filter AI provider returned no usable payload. Error={Error}")]
    private partial void LogNoUsablePayload(string? error);

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Debug,
        Message = "Grid filter AI response failed to parse as JSON. Raw={Raw}")]
    private partial void LogJsonParseFailed(Exception ex, string raw);
}
