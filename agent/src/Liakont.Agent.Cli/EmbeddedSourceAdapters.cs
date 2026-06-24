namespace Liakont.Agent.Cli;

using System;
using Liakont.Agent.Adapters.DemoErpA;
using Liakont.Agent.Adapters.DemoErpB;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Security;

/// <summary>
/// Registre UNIQUE des adaptateurs source EMBARQUÉS dans cette version de l'agent. Source de vérité
/// partagée par le CLI (« adaptateur connu » de check-config) et l'installeur (menu source du wizard),
/// pour éviter deux listes parallèles qui dériveraient (CLAUDE.md n°6). Expose AUSSI la fabrique du
/// cycle de run (AGT02, ADR-0031) : seuls les adaptateurs dotés d'un canal de configuration
/// (<c>adapterConfig</c>) sont câblés au run ; les autres restent reconnus de check-config mais non
/// exécutables tant que leur lot ADP ne les a pas câblés (jamais d'exécution muette — CLAUDE.md n°3).
/// </summary>
internal static class EmbeddedSourceAdapters
{
    /// <summary>Noms (SourceName) des adaptateurs source embarqués reconnus par check-config.</summary>
    public static string[] Names() => new[]
    {
        EncheresV6ExtractorFactory.AdapterName,
        DemoErpAExtractorFactory.AdapterName,
        DemoErpBExtractorFactory.AdapterName,
    };

    /// <summary>
    /// Crée l'extracteur CONFIGURÉ pour le cycle de run (AGT02) à partir de <c>agent.json</c>. Lève
    /// <see cref="AgentConfigException"/> (français) si l'adaptateur n'est pas câblé au run dans cette version.
    /// </summary>
    /// <param name="adapter">Nom de l'adaptateur (valeur de <c>extraction.adapter</c>).</param>
    /// <param name="config">Configuration de l'agent chargée.</param>
    /// <param name="protector">Déchiffreur de secrets (DPAPI).</param>
    /// <param name="log">Journal de l'agent (quarantaine d'un document source malformé).</param>
    /// <returns>L'extracteur configuré.</returns>
    public static IExtractor CreateConfigured(string adapter, AgentConfig config, ISecretProtector protector, IAgentLog log)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (protector is null)
        {
            throw new ArgumentNullException(nameof(protector));
        }

        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (string.Equals(adapter, DemoErpAExtractorFactory.AdapterName, StringComparison.OrdinalIgnoreCase))
        {
            return DemoErpAExtractorFactory.Create(config, protector, log);
        }

        if (string.Equals(adapter, DemoErpBExtractorFactory.AdapterName, StringComparison.OrdinalIgnoreCase))
        {
            return DemoErpBExtractorFactory.Create(config, protector, log);
        }

        if (string.Equals(adapter, EncheresV6ExtractorFactory.AdapterName, StringComparison.OrdinalIgnoreCase))
        {
            return EncheresV6ExtractorFactory.Create(config, protector, log);
        }

        throw new AgentConfigException(
            $"L'adaptateur « {adapter} » n'est pas câblé au cycle d'extraction du service dans cette version. "
            + "Vérifiez extraction.adapter (EncheresV6, DemoErpA ou DemoErpB).");
    }
}
