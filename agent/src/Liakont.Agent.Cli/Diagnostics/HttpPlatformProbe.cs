namespace Liakont.Agent.Cli.Diagnostics;

using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Net;
using Newtonsoft.Json;

/// <summary>
/// Sonde « heartbeat à blanc » de la plateforme (F12 §2.1, §3, commande <c>test-api</c>) :
/// POST <c>/api/agent/v1/heartbeat</c> avec la clé API configurée, et interprète le code de réponse
/// du contrat (F12 §3.3) en diagnostic lisible. HTTPS sortant uniquement ; TLS 1.2+ forcé (net48,
/// F12 §2.6). Aucune donnée n'est poussée : seul l'état d'authentification/joignabilité est éprouvé.
/// </summary>
internal static class HttpPlatformProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Probe la plateforme. <paramref name="apiKey"/> est la clé DÉJÀ déchiffrée. Ne lève jamais d'exception.</summary>
    public static PlatformProbeResult Probe(string platformUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(platformUrl))
        {
            return new PlatformProbeResult(PlatformProbeStatus.Unreachable, "Aucune URL de plateforme n'est configurée.");
        }

        // TLS 1.2+ obligatoire : net48 négocie SSL3/TLS1.0 par défaut, refusés par les plateformes
        // modernes. Point centralisé (RDF01) — partagé avec le chemin de run réel et le Main de chaque exécutable.
        AgentTls.ForceStrongTls();

        string endpoint = platformUrl.TrimEnd('/') + "/api/agent/" + AgentContractVersion.Current + "/heartbeat";
        string agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        var heartbeat = new HeartbeatRequestDto(AgentContractVersion.ContractVersion, agentVersion, DateTime.UtcNow);
        string body = JsonConvert.SerializeObject(heartbeat);

        try
        {
            using (var client = new HttpClient { Timeout = Timeout })
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Headers.Add(AgentApiHeaders.AgentKey, apiKey);
                request.Headers.Add(AgentApiHeaders.ContractVersion, AgentContractVersion.ContractVersion);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    return Interpret(response.StatusCode);
                }
            }
        }
        catch (Exception ex)
        {
            // Pas de réponse HTTP du tout : DNS, réseau, TLS, certificat, ou délai dépassé.
            return new PlatformProbeResult(
                PlatformProbeStatus.Unreachable,
                $"Plateforme injoignable à « {endpoint} » : {ex.Message} Vérifiez l'URL, la connexion réseau et le certificat HTTPS.");
        }
    }

    private static PlatformProbeResult Interpret(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        if (code >= 200 && code <= 299)
        {
            return new PlatformProbeResult(PlatformProbeStatus.Ok, "Plateforme joignable et clé API acceptée.");
        }

        switch (code)
        {
            case 401:
                return new PlatformProbeResult(PlatformProbeStatus.InvalidKey, "Clé API invalide (401). Vérifiez la clé configurée dans agent.json.");
            case 403:
                return new PlatformProbeResult(PlatformProbeStatus.RevokedKey, "Clé API révoquée ou non autorisée (403). Demandez une nouvelle clé à l'éditeur.");
            case 426:
                return new PlatformProbeResult(PlatformProbeStatus.UpgradeRequired, "Version de l'agent non supportée par la plateforme (426). Une mise à jour de l'agent est requise.");
            default:
                return new PlatformProbeResult(PlatformProbeStatus.UnexpectedResponse, $"Réponse inattendue de la plateforme (HTTP {code}).");
        }
    }
}
