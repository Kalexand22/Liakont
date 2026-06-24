namespace Liakont.Agent.Installer.Configuration;

using System;
using System.Collections.Generic;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Security;
using Liakont.Agent.Installer.Profiles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Construit le contenu de <c>agent.json</c> à partir d'une <see cref="ResolvedConfiguration"/>, en
/// CHIFFRANT DPAPI les secrets (clé API et chaîne ODBC) via <see cref="ISecretProtector"/> avant écriture
/// (F13 §6, CLAUDE.md n°10 : jamais de secret en clair dans un fichier). Le schéma de sortie n'est PAS
/// redéfini ici : le JSON produit est re-validé par <see cref="AgentConfigLoader.Parse"/> (cœur agent) —
/// garde anti-dérive qui fait remonter immédiatement tout écart de schéma (platformUrl non HTTPS, heure
/// de planification mal formée…) sous forme d'<see cref="AgentConfigException"/>, plutôt que d'écrire un
/// agent.json que l'agent refuserait au démarrage.
/// </summary>
internal static class AgentJsonBuilder
{
    /// <summary>
    /// Clés de champ profilables que <see cref="Build"/> écrit RÉELLEMENT dans agent.json (source de
    /// vérité anti-faux-vert). Les clés du registre absentes d'ici (logging, autoUpdate, odbcAdvanced)
    /// ne sont PAS encore portées par le schéma agent.json du cœur agent (F12 / AgentConfigLoader) :
    /// le wizard ne doit donc pas les présenter comme saisissables, sinon la saisie serait silencieusement
    /// perdue. instanceName n'y figure pas non plus : il cible l'installation (service/chemins), pas le
    /// contenu de agent.json.
    /// </summary>
    internal static readonly IReadOnlyCollection<string> MappedFieldKeys = new[]
    {
        ProfileFieldKeys.PlatformUrl,
        ProfileFieldKeys.ApiKey,
        ProfileFieldKeys.Adapter,
        ProfileFieldKeys.OdbcConnection,
        ProfileFieldKeys.PdfPoolPath,
        ProfileFieldKeys.Schedule,
        ProfileFieldKeys.ExtractFromUtc,
        ProfileFieldKeys.Dossier,
        ProfileFieldKeys.SourceSchema,
    };

    /// <summary>
    /// Sérialise <paramref name="config"/> en <c>agent.json</c>, secrets chiffrés par
    /// <paramref name="protector"/>. Lève <see cref="AgentConfigException"/> si le résultat ne satisfait
    /// pas le schéma du cœur agent.
    /// </summary>
    public static string Build(ResolvedConfiguration config, ISecretProtector protector)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (protector == null)
        {
            throw new ArgumentNullException(nameof(protector));
        }

        string apiKeyClear = config.Get(ProfileFieldKeys.ApiKey) ?? string.Empty;
        string? odbcClear = config.Get(ProfileFieldKeys.OdbcConnection);
        string? pdfPool = config.Get(ProfileFieldKeys.PdfPoolPath);
        string? scheduleRaw = config.Get(ProfileFieldKeys.Schedule);

        var extraction = new JObject
        {
            ["adapter"] = config.Get(ProfileFieldKeys.Adapter) ?? string.Empty,
        };

        // Un secret VIDE n'est jamais « chiffré » (cela produirait une valeur non vide qui passerait à
        // tort la validation « présent ») : on laisse le champ absent/vide, et le round-trip le rejette.
        if (!string.IsNullOrWhiteSpace(odbcClear))
        {
            extraction["odbcConnectionString"] = protector.Protect(odbcClear!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(pdfPool))
        {
            extraction["pdfPoolPath"] = pdfPool!.Trim();
        }

        extraction["schedule"] = BuildScheduleArray(scheduleRaw);
        extraction["catchUpOnStart"] = false;

        // Borne « extraire depuis » (factures à partir de cette date) : OPTIONNELLE. Vide = aucun rattrapage
        // d'historique (fenêtre depuis maintenant, uniquement les nouveaux documents — ADR-0031). Transmise
        // telle quelle ; le format date/heure est validé par le chargeur du cœur agent (re-validation ci-dessous).
        string? extractFromClear = config.Get(ProfileFieldKeys.ExtractFromUtc);
        if (!string.IsNullOrWhiteSpace(extractFromClear))
        {
            extraction["extractFromUtc"] = extractFromClear!.Trim();
        }

        var root = new JObject
        {
            ["platformUrl"] = config.Get(ProfileFieldKeys.PlatformUrl) ?? string.Empty,
            ["apiKey"] = string.IsNullOrWhiteSpace(apiKeyClear) ? string.Empty : protector.Protect(apiKeyClear.Trim()),
            ["heartbeatMinutes"] = AgentConfigLoader.DefaultHeartbeatMinutes,
            ["extraction"] = extraction,
        };

        // adapterConfig.<adaptateur> : configuration SPÉCIFIQUE à l'adaptateur sélectionné (ex. EncheresV6 :
        // « dossier » = filtre tenant, « schema » = préfixe SQL source). Écrite sous le NOM de l'adaptateur
        // SÉLECTIONNÉ (générique, jamais codé en dur) ; seules les valeurs renseignées sont émises (un adaptateur
        // sans config spécifique n'a pas de bloc). La FABRIQUE de l'adaptateur valide ces champs au run.
        var adapterConfig = new JObject();
        string? dossier = config.Get(ProfileFieldKeys.Dossier);
        if (!string.IsNullOrWhiteSpace(dossier))
        {
            adapterConfig["dossier"] = dossier!.Trim();
        }

        string? sourceSchema = config.Get(ProfileFieldKeys.SourceSchema);
        if (!string.IsNullOrWhiteSpace(sourceSchema))
        {
            adapterConfig["schema"] = sourceSchema!.Trim();
        }

        string adapterName = config.Get(ProfileFieldKeys.Adapter) ?? string.Empty;
        if (adapterConfig.HasValues && !string.IsNullOrWhiteSpace(adapterName))
        {
            root["adapterConfig"] = new JObject { [adapterName] = adapterConfig };
        }

        string json = root.ToString(Formatting.Indented);

        // Garde anti-dérive : re-valider par le chargeur du cœur agent (lève AgentConfigException si le
        // schéma n'est pas respecté). On ignore volontairement la AgentConfig retournée (seule la
        // validation nous intéresse ici).
        _ = AgentConfigLoader.Parse(json, "agent.json");
        return json;
    }

    // Le champ « schedule » du profil/saisie est une liste d'heures HH:mm séparées par des virgules
    // (ex. « 03:00,13:00 ») ; agent.json attend un tableau. La validité de chaque heure (HH:mm sur 24 h)
    // est contrôlée par AgentConfigLoader au round-trip — on ne la redéfinit pas ici.
    private static JArray BuildScheduleArray(string? scheduleRaw)
    {
        var array = new JArray();
        if (string.IsNullOrWhiteSpace(scheduleRaw))
        {
            return array;
        }

        foreach (string part in scheduleRaw!.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                array.Add(trimmed);
            }
        }

        return array;
    }
}
