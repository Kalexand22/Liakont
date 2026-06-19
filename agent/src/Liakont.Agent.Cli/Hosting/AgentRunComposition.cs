namespace Liakont.Agent.Cli.Hosting;

using System;
using System.Net.Http;
using System.Security.Cryptography;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Hosting;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Security;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Time;
using Liakont.Agent.Core.Transport;

/// <summary>
/// Composition root du CYCLE DE RUN RÉEL de l'agent (AGT02, ADR-0031), partagée par la commande CLI
/// <c>run</c> et le service Windows. Charge <c>agent.json</c>, déchiffre les secrets (DPAPI, ICI
/// seulement — jamais journalisés, CLAUDE.md n°10), résout l'adaptateur configuré et assemble
/// extraction → file locale → drainage (push + réconciliation) → journal de run. Aucune logique métier
/// (CLAUDE.md n°6) : il pose, câble, libère. Lève <see cref="AgentConfigException"/> si la configuration
/// est absente/invalide ou si l'adaptateur n'est pas câblé au run — bloquer plutôt que tourner faux (n°3).
/// </summary>
internal static class AgentRunComposition
{
    /// <summary>Construit le cycle de run composé à partir de la configuration de l'instance courante.</summary>
    /// <param name="log">Journal de l'agent (fichier).</param>
    /// <returns>Le cycle composé et ses ressources jetables.</returns>
    public static ComposedRunCycle Build(IAgentLog log)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        AgentConfig config = AgentConfigLoader.Load(AgentPaths.ConfigPath);
        var protector = new DpapiSecretProtector();
        var clock = new SystemClock();

        // Adaptateur configuré (déchiffre la chaîne ODBC, lit adapterConfig) + clé API, résolus AVANT
        // d'ouvrir des ressources. Le déchiffrement DPAPI peut échouer si agent.json vient d'une AUTRE
        // machine/compte (blob lié à la machine) ou si un secret a été collé en clair : on traduit ces
        // échecs en AgentConfigException (message FR) pour que le service/CLI bascule proprement, plutôt
        // que de planter en boucle de redémarrage SCM (codex P2). Un adaptateur inconnu lève déjà une
        // AgentConfigException (laissée passer par le filtre).
        IExtractor extractor;
        string apiKey;
        try
        {
            extractor = EmbeddedSourceAdapters.CreateConfigured(config.Extraction.Adapter, config, protector, log);
            apiKey = protector.Unprotect(config.ApiKeyProtected);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
        {
            throw new AgentConfigException(
                "Les secrets de agent.json (clé API ou chaîne ODBC) sont illisibles sur ce poste — re-chiffrez-les "
                + $"ici avec « liakont-agent-cli encrypt » (un blob DPAPI est lié à la machine et au compte). Détail : {ex.Message}");
        }

        var queue = new LocalQueue(AgentPaths.DatabasePath, clock);
        HttpClient? httpClient = null;
        try
        {
            httpClient = CreateHttpClient(config.PlatformUrl);
            var platformClient = new HttpPlatformClient(httpClient, apiKey);
            var extractionCycle = new ExtractionCycle(queue, log);
            var drainer = new QueueDrainer(queue, platformClient, log);
            var journal = new AgentRunJournal(queue);
            var cycle = new AgentRunCycle(
                extractor,
                extractionCycle,
                drainer,
                queue,
                clock,
                log,
                journal,
                autoUpdate: null,
                extractFromUtc: config.Extraction.ExtractFromUtc);
            return new ComposedRunCycle(cycle, queue, httpClient);
        }
        catch
        {
            // Toute défaillance après l'ouverture de la file/du client HTTP libère les deux (pas de fuite).
            queue.Dispose();
            httpClient?.Dispose();
            throw;
        }
    }

    private static HttpClient CreateHttpClient(string platformUrl)
    {
        // BaseAddress doit se terminer par « / » pour que les chemins relatifs du client
        // (api/agent/v1/...) se résolvent correctement.
        string baseUrl = platformUrl.EndsWith("/", StringComparison.Ordinal) ? platformUrl : platformUrl + "/";
        return new HttpClient { BaseAddress = new Uri(baseUrl, UriKind.Absolute) };
    }
}
