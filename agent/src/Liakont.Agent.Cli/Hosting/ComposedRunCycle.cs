namespace Liakont.Agent.Cli.Hosting;

using System;
using System.Net.Http;
using Liakont.Agent.Core.Hosting;
using Liakont.Agent.Core.Storage;

/// <summary>
/// Tient le cycle de run COMPOSÉ (AGT02, ADR-0023) et ses ressources jetables (file locale SQLite,
/// client HTTP) pour une libération propre. Le service Windows le garde le temps de sa vie ; la commande
/// CLI <c>run</c> le libère après un run unique.
/// </summary>
internal sealed class ComposedRunCycle : IDisposable
{
    private readonly LocalQueue _queue;
    private readonly HttpClient _httpClient;

    /// <summary>Crée le porteur du cycle composé et de ses ressources.</summary>
    /// <param name="cycle">Le cycle de run prêt à être exécuté.</param>
    /// <param name="queue">La file locale (à libérer).</param>
    /// <param name="httpClient">Le client HTTP de la plateforme (à libérer).</param>
    public ComposedRunCycle(AgentRunCycle cycle, LocalQueue queue, HttpClient httpClient)
    {
        Cycle = cycle ?? throw new ArgumentNullException(nameof(cycle));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>Le cycle de run à exécuter (<see cref="AgentRunCycle.Run"/>).</summary>
    public AgentRunCycle Cycle { get; }

    /// <summary>Libère la file locale et le client HTTP.</summary>
    public void Dispose()
    {
        _queue.Dispose();
        _httpClient.Dispose();
    }
}
