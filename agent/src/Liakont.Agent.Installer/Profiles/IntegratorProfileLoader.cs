namespace Liakont.Agent.Installer.Profiles;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Charge un profil intégrateur depuis son manifeste JSON (F13 §5.2 ; le profil est sérialisé en JSON,
/// le sérialiseur déjà au catalogue agent). Ne fait QUE l'interprétation structurelle : JSON malformé,
/// bloc mal typé ou « etat » inconnu lèvent une <see cref="ProfileFormatException"/>. Les règles de
/// SCHÉMA (clé inconnue, masqué sans valeur, requis non résolu, secret imposé, nom d'instance invalide)
/// sont du ressort de <see cref="ProfileValidator"/> — qui suppose un profil bien formé.
/// </summary>
internal static class IntegratorProfileLoader
{
    private const string AcceptedStates = "« affiché », « verrouillé » ou « masqué »";

    /// <summary>Charge et interprète le profil situé à <paramref name="path"/>.</summary>
    public static IntegratorProfile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Chemin de profil vide.", nameof(path));
        }

        string json = File.ReadAllText(path);
        return Parse(json, Path.GetFileName(path));
    }

    /// <summary>
    /// Interprète <paramref name="json"/> en profil. <paramref name="sourceName"/> n'apparaît que dans
    /// les messages d'erreur (nom du fichier, ou « (en mémoire) » pour un test).
    /// </summary>
    public static IntegratorProfile Parse(string json, string sourceName)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ProfileFormatException(
                $"Profil « {sourceName} » : JSON illisible ({ex.Message}).", ex);
        }

        string profileName = (root.Value<string>("profil") ?? string.Empty).Trim();
        if (profileName.Length == 0)
        {
            profileName = "(sans nom)";
        }

        IntegratorBranding branding = ParseBranding(root["branding"], sourceName);
        Dictionary<string, FieldDeclaration> fields = ParseFields(root["champs"], sourceName);

        return new IntegratorProfile(profileName, branding, fields);
    }

    private static IntegratorBranding ParseBranding(JToken? token, string sourceName)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return IntegratorBranding.Empty;
        }

        if (token.Type != JTokenType.Object)
        {
            throw new ProfileFormatException(
                $"Profil « {sourceName} » : le bloc « branding » doit être un objet.");
        }

        var obj = (JObject)token;
        return new IntegratorBranding(
            obj.Value<string>("nom"),
            obj.Value<string>("logo"),
            obj.Value<string>("couleurPrincipale"));
    }

    private static Dictionary<string, FieldDeclaration> ParseFields(JToken? token, string sourceName)
    {
        var fields = new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal);
        if (token == null || token.Type == JTokenType.Null)
        {
            return fields;
        }

        if (token.Type != JTokenType.Object)
        {
            throw new ProfileFormatException(
                $"Profil « {sourceName} » : le bloc « champs » doit être un objet.");
        }

        foreach (JProperty property in ((JObject)token).Properties())
        {
            fields[property.Name] = ParseDeclaration(property.Name, property.Value, sourceName);
        }

        return fields;
    }

    private static FieldDeclaration ParseDeclaration(string key, JToken token, string sourceName)
    {
        if (token.Type != JTokenType.Object)
        {
            throw new ProfileFormatException(
                $"Profil « {sourceName} » : le champ « {key} » doit être un objet {{ etat, valeur }}.");
        }

        var obj = (JObject)token;

        string? rawState = obj.Value<string>("etat");
        if (string.IsNullOrWhiteSpace(rawState))
        {
            throw new ProfileFormatException(
                $"Profil « {sourceName} » : le champ « {key} » n'a pas d'« etat ». Attendu : {AcceptedStates}.");
        }

        FieldState state = ParseState(rawState!.Trim(), key, sourceName);
        string? value = ReadScalarValue(obj["valeur"], key, sourceName);
        return new FieldDeclaration(state, value);
    }

    private static FieldState ParseState(string rawState, string key, string sourceName)
    {
        // États canoniques français (F13 §5.2), insensibles à la casse. Une valeur inconnue est une
        // erreur de format explicite — pas un défaut silencieux (anti-faux-vert, lessons 2026-06-02).
        if (string.Equals(rawState, "affiché", StringComparison.OrdinalIgnoreCase))
        {
            return FieldState.Shown;
        }

        if (string.Equals(rawState, "verrouillé", StringComparison.OrdinalIgnoreCase))
        {
            return FieldState.Locked;
        }

        if (string.Equals(rawState, "masqué", StringComparison.OrdinalIgnoreCase))
        {
            return FieldState.Hidden;
        }

        throw new ProfileFormatException(
            $"Profil « {sourceName} » : état inconnu « {rawState} » pour le champ « {key} ». Attendu : {AcceptedStates}.");
    }

    private static string? ReadScalarValue(JToken? token, string key, string sourceName)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }

        switch (token.Type)
        {
            case JTokenType.String:
                return token.Value<string>();
            case JTokenType.Boolean:
                // true/false → « true »/« false » (invariant, minuscules) pour une valeur opaque stable.
                return token.Value<bool>() ? "true" : "false";
            case JTokenType.Integer:
            case JTokenType.Float:
                return token.Value<decimal>().ToString(CultureInfo.InvariantCulture);
            default:
                throw new ProfileFormatException(
                    $"Profil « {sourceName} » : la « valeur » du champ « {key} » doit être un scalaire (texte, booléen ou nombre).");
        }
    }
}
