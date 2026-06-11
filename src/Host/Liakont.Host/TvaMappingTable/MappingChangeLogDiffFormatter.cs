namespace Liakont.Host.TvaMappingTable;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Restitue, pour une entrée du journal append-only des modifications de mapping TVA
/// (<see cref="MappingChangeLogEntryDto"/>), un diff AVANT → APRÈS lisible (décision E2, lot FIX2). Les
/// valeurs avant/après sont déjà persistées en JSON par <c>MappingChangeLogFactory</c> (TVA05) : ce
/// formateur ne fait que les LIRE et les présenter — aucune migration, aucune logique fiscale, aucun
/// accès base (CLAUDE.md n°19). La « Composante » n'apparaît dans le diff QUE si le vertical enchères
/// est actif (E2 : aucune mention hors vertical). Un JSON absent / illisible donne un diff vide (jamais
/// d'exception côté page).
/// </summary>
internal static class MappingChangeLogDiffFormatter
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly IReadOnlyDictionary<string, JsonElement> Empty =
        new Dictionary<string, JsonElement>(System.StringComparer.Ordinal);

    // Champs d'une règle (ordre d'affichage) ; même structure JSON que MappingChangeLogFactory.SerializeRule.
    private static readonly FieldDef[] RuleFieldDefs =
    {
        new("SourceRegimeCode", "Régime source", StringValue),
        new("Label", "Libellé", StringValue),
        new("Part", TvaComposanteVocabulary.FieldLabel, ComposanteValue),
        new("Category", "Catégorie", StringValue),
        new("Vatex", "VATEX", StringValue),
        new("Note", "Note", StringValue),
        new("RateMode", "Mode de taux", RateModeValue),
        new("RateValue", "Taux", RateValue),
        new("SourceFlags", "Conditions sur flags source", FlagsValue),
    };

    // Champs d'une création de table (MappingChangeLogFactory.SerializeCreation).
    private static readonly FieldDef[] CreationFieldDefs =
    {
        new("MappingVersion", "Version", StringValue),
        new("DefaultBehavior", "Comportement par défaut", DefaultBehaviorValue),
        new("RuleCount", "Nombre de règles", StringValue),
        new("IsValidated", "Validée", BoolValue),
    };

    // Champs d'une validation (MappingChangeLogFactory.SerializeValidation).
    private static readonly FieldDef[] ValidationFieldDefs =
    {
        new("ValidatedBy", "Validé par", StringValue),
        new("ValidatedDate", "Date de validation", StringValue),
    };

    public static IReadOnlyList<MappingChangeDiffLine> Describe(
        MappingChangeLogEntryDto entry, bool auctionVerticalEnabled)
    {
        var ruleFields = RuleFields(auctionVerticalEnabled);
        return entry.ChangeType switch
        {
            "AddRule" => Side(ruleFields, entry.AfterJson, after: true),
            "RemoveRule" => Side(ruleFields, entry.BeforeJson, after: false),
            "UpdateRule" => Diff(ruleFields, entry.BeforeJson, entry.AfterJson),
            "Validate" => Diff(ValidationFieldDefs, entry.BeforeJson, entry.AfterJson),
            "CreateTable" => Side(CreationFieldDefs, entry.AfterJson, after: true),
            _ => System.Array.Empty<MappingChangeDiffLine>(),
        };
    }

    // La « Composante » (part) n'est exposée que vertical enchères actif (E2 : aucune mention sinon).
    private static FieldDef[] RuleFields(bool auctionVerticalEnabled) =>
        auctionVerticalEnabled
            ? RuleFieldDefs
            : RuleFieldDefs.Where(f => f.JsonName != "Part").ToArray();

    private static List<MappingChangeDiffLine> Side(
        IReadOnlyList<FieldDef> fields, string? json, bool after)
    {
        var obj = ParseObject(json);
        var lines = new List<MappingChangeDiffLine>();
        foreach (var field in fields)
        {
            if (!obj.TryGetValue(field.JsonName, out var element))
            {
                continue;
            }

            var value = field.Format(element);
            if (!string.IsNullOrEmpty(value))
            {
                lines.Add(after
                    ? new MappingChangeDiffLine(field.Label, null, value)
                    : new MappingChangeDiffLine(field.Label, value, null));
            }
        }

        return lines;
    }

    private static List<MappingChangeDiffLine> Diff(
        IReadOnlyList<FieldDef> fields, string? beforeJson, string? afterJson)
    {
        var before = ParseObject(beforeJson);
        var after = ParseObject(afterJson);
        var lines = new List<MappingChangeDiffLine>();
        foreach (var field in fields)
        {
            var b = before.TryGetValue(field.JsonName, out var be) ? field.Format(be) : null;
            var a = after.TryGetValue(field.JsonName, out var ae) ? field.Format(ae) : null;

            // Seuls les champs qui CHANGENT (présence ou valeur) apparaissent dans le diff.
            if (!string.Equals(b, a, System.StringComparison.Ordinal))
            {
                lines.Add(new MappingChangeDiffLine(field.Label, b, a));
            }
        }

        return lines;
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var dict = new Dictionary<string, JsonElement>(System.StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                // Clone : le JsonDocument est libéré à la sortie du using.
                dict[property.Name] = property.Value.Clone();
            }

            return dict;
        }
        catch (JsonException)
        {
            // Donnée d'audit illisible : on n'affiche simplement pas de diff (jamais une exception en page).
            return Empty;
        }
    }

    private static string? StringValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Null => null,
        JsonValueKind.Undefined => null,
        _ => element.ToString(),
    };

    private static string? ComposanteValue(JsonElement element)
    {
        var raw = StringValue(element);
        return string.IsNullOrEmpty(raw) ? null : TvaComposanteVocabulary.ValueLabel(raw);
    }

    private static string? RateModeValue(JsonElement element) => StringValue(element) switch
    {
        "Fixed" => "Taux fixe",
        "ComputedFromSource" => "Calculé depuis la source",
        var other => other,
    };

    private static string? RateValue(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var rate)
            ? string.Format(Fr, "{0:0.##} %", rate)
            : StringValue(element);

    private static string? DefaultBehaviorValue(JsonElement element) => StringValue(element) switch
    {
        "Block" => "Blocage (régime non mappé)",
        var other => other,
    };

    private static string? BoolValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => "Oui",
        JsonValueKind.False => "Non",
        _ => StringValue(element),
    };

    private static string? FlagsValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return StringValue(element);
        }

        var pairs = element.EnumerateObject()
            .Select(p => $"{p.Name} = {StringValue(p.Value)}")
            .ToList();
        return pairs.Count == 0 ? null : string.Join(", ", pairs);
    }

    private sealed record FieldDef(string JsonName, string Label, System.Func<JsonElement, string?> Format);
}
