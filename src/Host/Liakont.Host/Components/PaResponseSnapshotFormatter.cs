namespace Liakont.Host.Components;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Restitue en MESSAGE OPÉRATEUR lisible (CLAUDE.md n°12 ; « zéro JSON brut », F10 §1) le snapshot de réponse
/// d'une Plateforme Agréée, qu'il provienne d'un DOCUMENT rejeté (BUG-27 : enveloppe
/// <c>{ errors:[{Code,Message}], rawResponse, … }</c> produite par <c>SendPaSnapshot</c>) ou d'une ÉMISSION
/// e-reporting B2C (BUG-22 : réponse PA brute, souvent <c>{ http_status_code, message }</c>). Fonction PURE et
/// TOTALE : ne lève JAMAIS (un snapshot illisible ou non-JSON est restitué tel quel, tronqué), n'invente aucun
/// message (les motifs viennent de la PA, jamais fabriqués — CLAUDE.md n°2). Retourne les lignes du motif, ou
/// une liste VIDE si le snapshot est absent ou ne porte aucun motif exploitable.
/// </summary>
public static class PaResponseSnapshotFormatter
{
    /// <summary>Longueur maximale d'un message brut restitué (la réponse PA brute peut être volumineuse).</summary>
    private const int MaxRawLength = 500;

    /// <summary>Lignes lisibles du motif PA (« [Code] Message » ou message seul), vides si aucun motif.</summary>
    /// <param name="snapshot">Snapshot <c>pa_response_snapshot</c> (enveloppe JSON, JSON brut PA, ou texte), ou <c>null</c>.</param>
    public static IReadOnlyList<string> Format(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(snapshot);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new[] { Truncate(snapshot.Trim()) };
            }

            // 1) errors[] (document rejeté, SendPaSnapshot) : une ligne « [Code] Message » par erreur.
            var lines = new List<string>();
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var error in errors.EnumerateArray())
                {
                    var line = FormatError(error);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line!);
                    }
                }
            }

            if (lines.Count > 0)
            {
                return lines;
            }

            // 2) message (réponse PA brute d'émission, ex. { http_status_code, message }).
            if (TryGetNonEmptyString(root, "message", out var message))
            {
                return new[] { Truncate(message!.Trim()) };
            }

            // 3) rawResponse embarquée (enveloppe sans erreurs structurées) — seulement si lisible (jamais
            //    dumper un JSON imbriqué : F10 §1).
            if (TryGetNonEmptyString(root, "rawResponse", out var raw) && !LooksLikeJson(raw!))
            {
                return new[] { Truncate(raw!.Trim()) };
            }

            return Array.Empty<string>();
        }
        catch (JsonException)
        {
            // Réponse PA non-JSON (texte / XML / message brut) : restituée telle quelle, tronquée.
            return new[] { Truncate(snapshot.Trim()) };
        }
    }

    private static string? FormatError(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        if (error.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var code = GetString(error, "Code") ?? GetString(error, "code");
        var message = GetString(error, "Message") ?? GetString(error, "message");

        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return message!.Trim();
        }

        return string.IsNullOrWhiteSpace(message)
            ? $"[{code!.Trim()}]"
            : $"[{code!.Trim()}] {message!.Trim()}";
    }

    private static bool TryGetNonEmptyString(JsonElement root, string name, out string? value)
    {
        value = GetString(root, name);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string Truncate(string value) =>
        value.Length <= MaxRawLength ? value : value[..MaxRawLength] + "…";
}
