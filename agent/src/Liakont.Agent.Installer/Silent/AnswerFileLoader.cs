namespace Liakont.Agent.Installer.Silent;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Charge un FICHIER DE RÉPONSES du mode silencieux (F13 §3 : installation en masse partageant le moteur
/// du wizard) et le convertit en <see cref="InstallationInput"/>. Le fichier porte les valeurs tenant
/// (chaîne ODBC, clé API, URL…) sous la clé « valeurs », indexées par les mêmes clés de champ que le
/// profil. Une clé inconnue fait ÉCHOUER le chargement (anti-faux-vert) : on ne fournit jamais une saisie
/// muette à cause d'une faute de frappe. Ce fichier n'est PAS versionné (il porte des secrets en clair) ;
/// seul un EXEMPLE fictif vit dans config/exemples/ (CLAUDE.md n°7/10).
/// </summary>
internal static class AnswerFileLoader
{
    /// <summary>Charge et interprète le fichier de réponses situé à <paramref name="path"/>.</summary>
    public static InstallationInput Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Chemin du fichier de réponses vide.", nameof(path));
        }

        string json = File.ReadAllText(path);
        return Parse(json, Path.GetFileName(path));
    }

    /// <summary>
    /// Interprète <paramref name="json"/> en saisie d'installation. <paramref name="sourceName"/>
    /// n'apparaît que dans les messages d'erreur (nom du fichier, ou « (en mémoire) » pour un test).
    /// </summary>
    public static InstallationInput Parse(string json, string sourceName)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AnswerFileFormatException(
                $"Fichier de réponses « {sourceName} » : JSON illisible ({ex.Message}).", ex);
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        JToken? valeurs = root["valeurs"];
        if (valeurs == null || valeurs.Type == JTokenType.Null)
        {
            return new InstallationInput(values);
        }

        if (valeurs.Type != JTokenType.Object)
        {
            throw new AnswerFileFormatException(
                $"Fichier de réponses « {sourceName} » : le bloc « valeurs » doit être un objet.");
        }

        foreach (JProperty property in ((JObject)valeurs).Properties())
        {
            if (!ProfileFieldKeys.IsKnown(property.Name))
            {
                throw new AnswerFileFormatException(
                    $"Fichier de réponses « {sourceName} » : champ inconnu « {property.Name} ». " +
                    "Corrigez la clé (les clés sont celles du profil : adapter, platformUrl, apiKey, " +
                    "odbcConnection, schedule, instanceName…).");
            }

            values[property.Name] = ReadScalarValue(property.Value, property.Name, sourceName);
        }

        return new InstallationInput(values);
    }

    private static string? ReadScalarValue(JToken token, string key, string sourceName)
    {
        switch (token.Type)
        {
            case JTokenType.Null:
                return null;
            case JTokenType.String:
                return token.Value<string>();
            case JTokenType.Boolean:
                return token.Value<bool>() ? "true" : "false";
            case JTokenType.Integer:
            case JTokenType.Float:
                try
                {
                    return token.Value<decimal>().ToString(CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is OverflowException || ex is FormatException || ex is InvalidCastException)
                {
                    throw new AnswerFileFormatException(
                        $"Fichier de réponses « {sourceName} » : la valeur numérique du champ « {key} » est hors intervalle.", ex);
                }

            default:
                throw new AnswerFileFormatException(
                    $"Fichier de réponses « {sourceName} » : la valeur du champ « {key} » doit être un scalaire (texte, booléen ou nombre).");
        }
    }
}
