namespace Liakont.Agent;

using System;
using System.Threading;
using Liakont.Agent.Core.Hosting;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Composition de l'hôte d'exécution de l'agent (AGT01), partagée par le service Windows et le mode
/// console. AGT01 fournit l'enveloppe (thread de fond + arrêt propre) ; le run réel (extraction +
/// push + heartbeat) sera injecté par AGT02/AGT03. Ici le cycle est NEUTRE (aucune logique métier
/// dans l'agent — CLAUDE.md n°6).
/// </summary>
internal sealed class AgentHost : IDisposable
{
    private readonly AgentBackgroundRunner _runner;

    private AgentHost(AgentBackgroundRunner runner)
    {
        _runner = runner;
    }

    public static AgentHost Create(IAgentLog log)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        // Cycle neutre tant qu'AGT02 n'a pas câblé l'extraction/le push. L'hôte reste vivant et
        // démontre la barrière d'arrêt propre ; il ne touche à rien dans la source.
        Action<CancellationToken> runCycle = _ => { };
        var runner = new AgentBackgroundRunner(runCycle, TimeSpan.FromMinutes(1), log);
        return new AgentHost(runner);
    }

    public void Start() => _runner.Start();

    public bool Stop(TimeSpan gracePeriod) => _runner.Stop(gracePeriod);

    public void Dispose() => _runner.Dispose();
}
