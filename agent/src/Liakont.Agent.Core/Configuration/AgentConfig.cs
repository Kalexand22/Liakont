namespace Liakont.Agent.Core.Configuration;

using System;
using System.Collections.Generic;

/// <summary>
/// Configuration typée de l'agent (F12 §2.4), chargée et validée depuis <c>agent.json</c> par
/// <see cref="AgentConfigLoader"/>. Les secrets (clé API, chaîne ODBC) restent sous leur forme
/// PROTÉGÉE (DPAPI) ; ils ne sont déchiffrés qu'à l'usage via <see cref="Security.ISecretProtector"/>,
/// jamais journalisés ni réécrits en clair (CLAUDE.md n°10).
/// </summary>
public sealed class AgentConfig
{
    private readonly IReadOnlyDictionary<string, AdapterConfigSection> _adapterConfig;

    public AgentConfig(
        string platformUrl,
        string apiKeyProtected,
        ExtractionConfig extraction,
        int heartbeatMinutes,
        IReadOnlyDictionary<string, AdapterConfigSection>? adapterConfig = null)
    {
        PlatformUrl = platformUrl;
        ApiKeyProtected = apiKeyProtected;
        Extraction = extraction;
        HeartbeatMinutes = heartbeatMinutes;
        _adapterConfig = adapterConfig ?? new Dictionary<string, AdapterConfigSection>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>URL HTTPS de la plateforme (ex. <c>https://liakont.editeur-x.fr</c>).</summary>
    public string PlatformUrl { get; }

    /// <summary>Clé API de l'agent, chiffrée DPAPI (header <c>X-Agent-Key</c> une fois déchiffrée).</summary>
    public string ApiKeyProtected { get; }

    /// <summary>Section extraction (adaptateur, ODBC protégé, pool PDF, planification).</summary>
    public ExtractionConfig Extraction { get; }

    /// <summary>Période du heartbeat en minutes (défaut 15 si absent du fichier).</summary>
    public int HeartbeatMinutes { get; }

    /// <summary>
    /// Configuration spécifique de l'adaptateur <paramref name="adapterName"/> (section
    /// <c>adapterConfig.&lt;nom&gt;</c>, ADR-0023). Retourne une section VIDE si rien n'est fourni —
    /// c'est la fabrique de l'adaptateur qui décide quels paramètres sont obligatoires (le chargeur
    /// reste générique : il ne connaît aucun champ propre à un adaptateur).
    /// </summary>
    public AdapterConfigSection GetAdapterConfig(string adapterName)
    {
        if (adapterName is null)
        {
            throw new ArgumentNullException(nameof(adapterName));
        }

        return _adapterConfig.TryGetValue(adapterName, out AdapterConfigSection? section)
            ? section
            : AdapterConfigSection.Empty(adapterName);
    }
}
