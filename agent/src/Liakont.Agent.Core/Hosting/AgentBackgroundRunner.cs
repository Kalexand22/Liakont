namespace Liakont.Agent.Core.Hosting;

using System;
using System.Diagnostics;
using System.Threading;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Hôte de fond de l'agent (AGT01) : possède le thread de travail et orchestre l'arrêt PROPRE via une
/// <see cref="GracefulRunGate"/>. Le contenu d'un cycle (extraction → collecte → push, planification
/// locale) est injecté — il sera fourni par AGT02/AGT03 ; AGT01 garantit l'enveloppe d'hébergement et
/// la barrière « un run en cours se termine avant l'arrêt ».
/// </summary>
public sealed class AgentBackgroundRunner : IDisposable
{
    private readonly Action<CancellationToken> _runCycle;
    private readonly TimeSpan _pollInterval;
    private readonly IAgentLog _log;
    private readonly GracefulRunGate _gate;
    private readonly string _threadName;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Thread? _thread;
    private int _started;
    private int _disposed;

    /// <param name="runCycle">Le cycle exécuté à chaque tick.</param>
    /// <param name="pollInterval">L'intervalle entre deux ticks (strictement positif).</param>
    /// <param name="log">Journal de l'agent.</param>
    /// <param name="gate">Barrière d'arrêt partagée (optionnelle).</param>
    /// <param name="threadName">
    /// Nom du thread de fond, pour distinguer plusieurs hôtes dans un dump de production (ex. extraction
    /// vs heartbeat). Défaut : <c>LiakontAgentRunner</c> (comportement historique).
    /// </param>
    public AgentBackgroundRunner(Action<CancellationToken> runCycle, TimeSpan pollInterval, IAgentLog log, GracefulRunGate? gate = null, string threadName = "LiakontAgentRunner")
    {
        _runCycle = runCycle ?? throw new ArgumentNullException(nameof(runCycle));
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "L'intervalle de cycle doit être strictement positif.");
        }

        _pollInterval = pollInterval;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _gate = gate ?? new GracefulRunGate();
        _threadName = string.IsNullOrWhiteSpace(threadName) ? "LiakontAgentRunner" : threadName;
    }

    /// <summary>Barrière d'arrêt partagée (le run réel, câblé plus tard, l'utilise pour s'enregistrer).</summary>
    public GracefulRunGate Gate => _gate;

    /// <summary>Démarre le thread de fond. Idempotence stricte : un seul démarrage par instance.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("L'hôte de l'agent est déjà démarré.");
        }

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = _threadName,
        };
        _thread.Start();
    }

    /// <summary>
    /// Demande l'arrêt et attend la fin du run EN COURS (au plus <paramref name="gracePeriod"/> AU TOTAL).
    /// Renvoie <c>true</c> si la file de travail est revenue au repos dans le délai imparti.
    /// <para>
    /// Le budget est borné à <paramref name="gracePeriod"/> pour les DEUX attentes (fin du run puis
    /// jonction du thread) : le service peut ainsi annoncer ce même budget au SCM sans risquer d'être
    /// tué pendant un arrêt propre légitime.
    /// </para>
    /// </summary>
    public bool Stop(TimeSpan gracePeriod)
    {
        _gate.RequestShutdown();
        _cts.Cancel();

        Stopwatch elapsed = Stopwatch.StartNew();
        bool idle = _gate.WaitForIdle(gracePeriod);

        TimeSpan remaining = gracePeriod - elapsed.Elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        _thread?.Join(remaining);
        return idle;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return; // Dispose idempotent
        }

        // Garantir l'arrêt du thread même si Dispose est appelé sans Stop préalable : l'annulation
        // réveille la boucle. La boucle est protégée contre l'ObjectDisposedException (course Dispose
        // vs thread encore vivant après une grâce dépassée).
        _gate.RequestShutdown();
        _cts.Cancel();
        _cts.Dispose();
    }

    private void Loop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                IDisposable? run = _gate.TryBeginRun();
                if (run == null)
                {
                    break; // arrêt demandé : ne pas démarrer un nouveau cycle
                }

                try
                {
                    _runCycle(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // arrêt en cours pendant le cycle : sortie propre
                }
                catch (Exception ex)
                {
                    _log.Error("Échec d'un cycle de l'agent.", ex);
                }
                finally
                {
                    run.Dispose();
                }

                // Attente interruptible jusqu'au prochain cycle (l'annulation réveille immédiatement).
                _cts.Token.WaitHandle.WaitOne(_pollInterval);
            }
        }
        catch (ObjectDisposedException)
        {
            // Course d'arrêt : l'hôte a été libéré (_cts.Dispose) pendant qu'un cycle dépassant la
            // grâce se terminait. Le thread sort silencieusement — aucune exception ne doit
            // s'échapper d'un thread de fond (sous .NET Framework, elle terminerait le process).
        }
    }
}
