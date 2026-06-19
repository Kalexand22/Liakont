namespace Liakont.Agent.Adapters.DemoErpB;

using System;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Security;

/// <summary>
/// Composition root de l'adaptateur DemoErpB : assemble un <see cref="IExtractor"/> configuré à partir de
/// <c>agent.json</c> (ADR-0023). Déchiffre la chaîne ODBC (DPAPI) ICI seulement et lit la section
/// <c>adapterConfig.DemoErpB</c> (émetteur, nature d'opération). Une configuration incomplète bloque
/// (jamais de démarrage muet — CLAUDE.md n°3).
/// </summary>
public static class DemoErpBExtractorFactory
{
    /// <summary>Nom de l'adaptateur (valeur de <c>extraction.adapter</c> et clé de <c>adapterConfig</c>).</summary>
    public const string AdapterName = "DemoErpB";

    /// <summary>Crée un extracteur DemoErpB configuré.</summary>
    /// <param name="config">La configuration de l'agent (chargée depuis agent.json).</param>
    /// <param name="protector">Le déchiffreur de secrets (DPAPI).</param>
    /// <param name="log">Le journal de l'agent (quarantaine d'un document source malformé).</param>
    /// <returns>L'extracteur prêt à l'emploi.</returns>
    public static IExtractor Create(AgentConfig config, ISecretProtector protector, IAgentLog log)
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

        string? protectedOdbc = config.Extraction.OdbcConnectionStringProtected;
        if (string.IsNullOrWhiteSpace(protectedOdbc))
        {
            throw new AgentConfigException(
                "L'adaptateur « DemoErpB » exige une chaîne ODBC (extraction.odbcConnectionString) : "
                + "renseignez-la dans agent.json (chiffrée DPAPI).");
        }

        string connectionString = protector.Unprotect(protectedOdbc!);
        var connectionFactory = new OdbcSourceConnectionFactory(connectionString);
        return new DemoErpBExtractor(connectionFactory, log);
    }
}
