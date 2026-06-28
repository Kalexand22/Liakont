namespace Liakont.Agent.Cli.Hosting;

using System;
using System.Net.Http;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Hosting;
using Liakont.Agent.Core.Storage;

/// <summary>
/// Tient le cycle de run COMPOSÉ (AGT02, ADR-0031) et ses ressources jetables (file locale SQLite,
/// client HTTP) pour une libération propre. Le service Windows le garde le temps de sa vie ; la commande
/// CLI <c>run</c> le libère après un run unique.
/// <para>
/// Porte AUSSI le <see cref="HeartbeatReporter"/> (AGT03 §1, F12 §2.5) et sa cadence : le service émet le
/// heartbeat périodique à ce rythme, INDÉPENDAMMENT des runs d'extraction (« même hors run »). Le
/// rapporteur réutilise le MÊME client plateforme et la MÊME file que le cycle de run (aucune ressource
/// supplémentaire à libérer).
/// </para>
/// </summary>
internal sealed class ComposedRunCycle : IDisposable
{
    private readonly LocalQueue _queue;
    private readonly HttpClient _httpClient;

    /// <summary>Crée le porteur du cycle composé et de ses ressources.</summary>
    /// <param name="cycle">Le cycle de run prêt à être exécuté.</param>
    /// <param name="queue">La file locale (à libérer).</param>
    /// <param name="httpClient">Le client HTTP de la plateforme (à libérer).</param>
    /// <param name="heartbeat">Le rapporteur de heartbeat (réutilise <paramref name="queue"/> et <paramref name="httpClient"/>).</param>
    /// <param name="heartbeatInterval">La cadence du heartbeat périodique (<c>heartbeatMinutes</c>, F12 §2.5).</param>
    public ComposedRunCycle(AgentRunCycle cycle, LocalQueue queue, HttpClient httpClient, HeartbeatReporter heartbeat, TimeSpan heartbeatInterval)
    {
        Cycle = cycle ?? throw new ArgumentNullException(nameof(cycle));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Heartbeat = heartbeat ?? throw new ArgumentNullException(nameof(heartbeat));
        if (heartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), "La cadence du heartbeat doit être strictement positive.");
        }

        HeartbeatInterval = heartbeatInterval;
    }

    /// <summary>Le cycle de run à exécuter (<see cref="AgentRunCycle.Run"/>).</summary>
    public AgentRunCycle Cycle { get; }

    /// <summary>Le rapporteur de heartbeat périodique (F12 §2.5, AGT03 §1).</summary>
    public HeartbeatReporter Heartbeat { get; }

    /// <summary>Cadence du heartbeat périodique (<c>heartbeatMinutes</c>).</summary>
    public TimeSpan HeartbeatInterval { get; }

    /// <summary>Libère la file locale et le client HTTP.</summary>
    public void Dispose()
    {
        _queue.Dispose();
        _httpClient.Dispose();
    }
}
