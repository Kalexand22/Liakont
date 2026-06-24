namespace Liakont.Agent;

using System;
using System.Threading;
using Liakont.Agent.Cli.Hosting;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Hosting;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Composition de l'hôte d'exécution de l'agent (AGT01), partagée par le service Windows et le mode
/// console. AGT01 fournit l'enveloppe (thread de fond + arrêt propre) ; le run réel (extraction → push)
/// est composé par AGT02 (ADR-0031) via <see cref="AgentRunComposition"/>, le MÊME composition root que
/// la commande CLI <c>run</c>. L'agent n'a aucune logique métier (CLAUDE.md n°6) : l'hôte ne fait que
/// poser le cycle et garantir l'arrêt propre.
/// </summary>
internal sealed class AgentHost : IDisposable
{
    // Le heartbeat est léger (lecture seule + un POST) : à l'arrêt on lui laisse un court budget, puis on
    // donne tout le reste de la grâce au cycle d'extraction pour qu'un run en cours se termine (F12 §2.2).
    // Exposé en secondes pour que le service l'INTÈGRE à son annonce d'arrêt au SCM (sinon l'arrêt
    // séquentiel heartbeat-puis-run pourrait consommer toute la marge annoncée et risquer un kill SCM).
    internal const int HeartbeatStopGraceSeconds = 5;

    // Le cycle du service acquiert le MÊME verrou inter-process (mutex par instance) que la commande CLI
    // `run` : un run de diagnostic lancé pendant que le service tourne ne s'exécute pas en parallèle (double
    // extraction/push + contention sur la file SQLite). Verrou déjà détenu → cycle SAUTÉ, le prochain tick
    // réessaiera (codex P2).
    private static readonly TimeSpan RunLockAcquireTimeout = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan HeartbeatStopGrace = TimeSpan.FromSeconds(HeartbeatStopGraceSeconds);

    private readonly AgentBackgroundRunner _runner;
    private readonly AgentBackgroundRunner? _heartbeatRunner;
    private readonly IDisposable? _composed;

    private AgentHost(AgentBackgroundRunner runner, AgentBackgroundRunner? heartbeatRunner, IDisposable? composed)
    {
        _runner = runner;
        _heartbeatRunner = heartbeatRunner;
        _composed = composed;
    }

    public static AgentHost Create(IAgentLog log)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        // AGT02 (ADR-0031) : compose le cycle réel (extraction → file locale → push) depuis agent.json.
        // TOUT échec de composition — config absente/invalide, secrets illisibles (déchiffrement DPAPI),
        // file locale SQLite corrompue/verrouillée, URL de plateforme invalide… — bascule en cycle NEUTRE
        // journalisé plutôt que d'abattre le service (pas de boucle de redémarrage SCM ; codex P2). La cause
        // complète est dans le journal ; l'opérateur voit pourquoi rien n'est extrait (jamais d'extraction
        // muette avec une config/un environnement fautifs — CLAUDE.md n°3).
        Action<CancellationToken> runCycle;
        ComposedRunCycle? composed = null;
        AgentBackgroundRunner? heartbeatRunner = null;
        try
        {
            composed = AgentRunComposition.Build(log);
            ComposedRunCycle built = composed;
            runCycle = token => RunWithLock(built, log, token);

            // Heartbeat périodique (AGT03 §1, F12 §2.5) : émis à CADENCE PROPRE (heartbeatMinutes) et
            // INDÉPENDAMMENT des runs d'extraction (« même hors run »). Il ne prend PAS le verrou de run
            // (lecture seule, léger), donc un run d'extraction en cours ne le bloque pas. Le rapporteur ne
            // lève jamais (F12 §2.5) — un échec réseau est un WARN local, jamais une auto-alerte agent. Le
            // premier battement part dès le démarrage (l'hôte de fond exécute le délégué avant la 1re attente),
            // si bien que l'agent communique dès le 1er démarrage automatique, sans Restart-Service (RB16).
            heartbeatRunner = new AgentBackgroundRunner(
                _ => built.Heartbeat.SendHeartbeat(),
                built.HeartbeatInterval,
                log,
                threadName: "LiakontAgentHeartbeat");
        }
        catch (Exception ex)
        {
            log.Error(
                "Cycle d'extraction non démarré — la composition a échoué (configuration, secrets illisibles sur "
                + "ce poste, ou ressources locales). Corrigez agent.json / l'environnement puis redémarrez le service.",
                ex);
            runCycle = _ => { };
        }

        var runner = new AgentBackgroundRunner(runCycle, TimeSpan.FromMinutes(1), log);
        return new AgentHost(runner, heartbeatRunner, composed);
    }

    public void Start()
    {
        _heartbeatRunner?.Start();
        _runner.Start();
    }

    public bool Stop(TimeSpan gracePeriod)
    {
        _heartbeatRunner?.Stop(HeartbeatStopGrace);
        return _runner.Stop(gracePeriod);
    }

    public void Dispose()
    {
        _heartbeatRunner?.Dispose();
        _runner.Dispose();
        _composed?.Dispose();
    }

    private static void RunWithLock(ComposedRunCycle composed, IAgentLog log, CancellationToken token)
    {
        InterProcessRunLock? runLock = InterProcessRunLock.TryAcquire(RunLockAcquireTimeout, AgentPaths.Current.RunMutexName);
        if (runLock is null)
        {
            log.Info("Cycle d'extraction sauté — un run est déjà en cours (commande CLI ou autre processus).");
            return;
        }

        using (runLock)
        {
            composed.Cycle.Run(token);
        }
    }
}
