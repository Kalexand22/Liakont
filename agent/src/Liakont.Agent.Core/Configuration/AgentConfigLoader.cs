namespace Liakont.Agent.Core.Configuration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

/// <summary>
/// Chargement et validation de <c>agent.json</c> (F12 §2.4). La validation est exhaustive (tous les
/// problèmes sont collectés en une fois) et produit des messages opérateur FRANÇAIS nommant le champ
/// et l'action. Aucun secret n'est déchiffré ici : la clé API et la chaîne ODBC restent sous forme
/// protégée dans la <see cref="AgentConfig"/> retournée (le déchiffrement DPAPI est différé à l'usage).
/// </summary>
public static class AgentConfigLoader
{
    /// <summary>Valeur par défaut de la période du heartbeat (F12 §2.5) quand le champ est absent.</summary>
    public const int DefaultHeartbeatMinutes = 15;

    private static readonly Regex _scheduleEntry = new Regex(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    /// <summary>Charge la configuration depuis le fichier indiqué. Lève <see cref="AgentConfigException"/> en cas d'erreur.</summary>
    public static AgentConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin du fichier de configuration est requis.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new AgentConfigException(
                $"Le fichier de configuration de l'agent est introuvable : « {path} ». " +
                "Créez-le à partir du modèle agent.json (voir la documentation d'installation).");
        }

        string raw = File.ReadAllText(path);
        return Parse(raw, path);
    }

    /// <summary>Analyse et valide le contenu JSON (séparé de l'accès disque pour les tests).</summary>
    public static AgentConfig Parse(string json, string sourceName)
    {
        AgentConfigJson? dto;
        try
        {
            dto = JsonConvert.DeserializeObject<AgentConfigJson>(json);
        }
        catch (JsonException ex)
        {
            throw new AgentConfigException(
                $"Le fichier de configuration « {sourceName} » n'est pas un JSON valide : {ex.Message}. " +
                "Corrigez la syntaxe (guillemets, virgules) puis relancez.");
        }

        if (dto is null)
        {
            throw new AgentConfigException(
                $"Le fichier de configuration « {sourceName} » est vide. " +
                "Renseignez au minimum platformUrl, apiKey et extraction.adapter.");
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(dto.PlatformUrl))
        {
            errors.Add("Le champ « platformUrl » est absent. Indiquez l'URL HTTPS de la plateforme (ex. https://liakont.editeur-x.fr).");
        }
        else if (!Uri.TryCreate(dto.PlatformUrl, UriKind.Absolute, out Uri? uri) || !IsSecurePlatformUri(uri))
        {
            errors.Add($"Le champ « platformUrl » (« {dto.PlatformUrl} ») doit être une URL HTTPS absolue (http n'est toléré que sur la boucle locale, pour le diagnostic). Corrigez-le (ex. https://liakont.editeur-x.fr).");
        }

        if (string.IsNullOrWhiteSpace(dto.ApiKey))
        {
            errors.Add("Le champ « apiKey » est absent. Collez la clé API chiffrée produite par « liakont-agent encrypt ».");
        }

        int heartbeatMinutes = DefaultHeartbeatMinutes;
        if (dto.HeartbeatMinutes.HasValue)
        {
            if (dto.HeartbeatMinutes.Value <= 0)
            {
                errors.Add($"Le champ « heartbeatMinutes » doit être un entier positif (valeur reçue : {dto.HeartbeatMinutes.Value}). Indiquez par exemple 15.");
            }
            else
            {
                heartbeatMinutes = dto.HeartbeatMinutes.Value;
            }
        }

        ExtractionConfig? extraction = ValidateExtraction(dto.Extraction, errors);

        if (errors.Count > 0)
        {
            throw new AgentConfigException(errors);
        }

        return new AgentConfig(
            dto.PlatformUrl!.Trim(),
            dto.ApiKey!.Trim(),
            extraction!,
            heartbeatMinutes,
            BuildAdapterConfig(dto.AdapterConfig));
    }

    // Transporte la section adapterConfig (ADR-0023) SANS connaître les champs d'un adaptateur : un
    // dictionnaire nom d'adaptateur → (clé → valeur), toutes les clés insensibles à la casse. La
    // validation (champs requis, valeurs autorisées) est déléguée à la fabrique de chaque adaptateur.
    private static Dictionary<string, AdapterConfigSection> BuildAdapterConfig(
        Dictionary<string, Dictionary<string, string>>? raw)
    {
        var result = new Dictionary<string, AdapterConfigSection>(StringComparer.OrdinalIgnoreCase);
        if (raw is null)
        {
            return result;
        }

        foreach (KeyValuePair<string, Dictionary<string, string>> entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null)
            {
                continue;
            }

            var values = new Dictionary<string, string>(entry.Value, StringComparer.OrdinalIgnoreCase);
            result[entry.Key] = new AdapterConfigSection(entry.Key, values);
        }

        return result;
    }

    private static ExtractionConfig? ValidateExtraction(ExtractionJson? extraction, List<string> errors)
    {
        if (extraction is null)
        {
            errors.Add("La section « extraction » est absente. Renseignez au minimum extraction.adapter.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(extraction.Adapter))
        {
            errors.Add("Le champ « extraction.adapter » est absent. Indiquez l'adaptateur source (ex. EncheresV6).");
        }

        var schedule = new List<string>();
        if (extraction.Schedule != null)
        {
            foreach (string entry in extraction.Schedule)
            {
                if (entry != null && _scheduleEntry.IsMatch(entry.Trim()))
                {
                    schedule.Add(entry.Trim());
                }
                else
                {
                    errors.Add($"Le champ « extraction.schedule » contient une heure invalide (« {entry} »). Attendu : HH:mm sur 24 h (ex. 03:00).");
                }
            }
        }

        DateTime? extractFromUtc = null;
        if (!string.IsNullOrWhiteSpace(extraction.ExtractFromUtc))
        {
            if (DateTime.TryParse(
                    extraction.ExtractFromUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out DateTime parsed))
            {
                extractFromUtc = parsed;
            }
            else
            {
                errors.Add(
                    $"Le champ « extraction.extractFromUtc » (« {extraction.ExtractFromUtc} ») n'est pas une date/heure "
                    + "ISO 8601 valide (ex. 2026-01-01T00:00:00Z).");
            }
        }

        if (string.IsNullOrWhiteSpace(extraction.Adapter))
        {
            return null;
        }

        return new ExtractionConfig(
            extraction.Adapter!.Trim(),
            string.IsNullOrWhiteSpace(extraction.OdbcConnectionString) ? null : extraction.OdbcConnectionString!.Trim(),
            string.IsNullOrWhiteSpace(extraction.PdfPoolPath) ? null : extraction.PdfPoolPath!.Trim(),
            schedule,
            extraction.CatchUpOnStart ?? false,
            string.IsNullOrWhiteSpace(extraction.FixturesPath) ? null : extraction.FixturesPath!.Trim(),
            extractFromUtc);
    }

    // HTTPS sortant uniquement (F12 §2.6) : la clé API (header X-Agent-Key) et les payloads fiscaux
    // ne doivent jamais transiter en clair (CLAUDE.md n°10). http n'est toléré que sur la boucle
    // locale (diagnostic / démo Liakont.Host local) — un trafic loopback ne quitte jamais la machine.
    private static bool IsSecurePlatformUri(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }

    // DTO de désérialisation (tolérant : tous les champs nullables, validés ensuite). Privé : la
    // surface publique est la AgentConfig validée, jamais ce miroir brut du fichier.
    private sealed class AgentConfigJson
    {
        [JsonProperty("platformUrl")]
        public string? PlatformUrl { get; set; }

        [JsonProperty("apiKey")]
        public string? ApiKey { get; set; }

        [JsonProperty("extraction")]
        public ExtractionJson? Extraction { get; set; }

        [JsonProperty("heartbeatMinutes")]
        public int? HeartbeatMinutes { get; set; }

        // Section adapterConfig (ADR-0023) : nom d'adaptateur → (clé → valeur, chaînes). Transportée
        // telle quelle ; les champs propres à un adaptateur sont validés par sa fabrique, pas ici.
        [JsonProperty("adapterConfig")]
        public Dictionary<string, Dictionary<string, string>>? AdapterConfig { get; set; }
    }

    private sealed class ExtractionJson
    {
        [JsonProperty("adapter")]
        public string? Adapter { get; set; }

        [JsonProperty("odbcConnectionString")]
        public string? OdbcConnectionString { get; set; }

        [JsonProperty("pdfPoolPath")]
        public string? PdfPoolPath { get; set; }

        [JsonProperty("fixturesPath")]
        public string? FixturesPath { get; set; }

        [JsonProperty("schedule")]
        public IList<string>? Schedule { get; set; }

        [JsonProperty("catchUpOnStart")]
        public bool? CatchUpOnStart { get; set; }

        [JsonProperty("extractFromUtc")]
        public string? ExtractFromUtc { get; set; }
    }
}
