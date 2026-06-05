namespace Liakont.Agent.Core.Hosting;

using System;
using System.Threading;

/// <summary>
/// Barrière d'arrêt propre (F12 §2.2, AGT01) : garantit qu'un run d'extraction EN COURS se termine
/// avant l'arrêt du service, et qu'AUCUN nouveau run ne démarre après une demande d'arrêt.
/// <para>
/// Sans dépendance à l'horloge murale ni à une primitive jetable : <see cref="WaitForIdle"/> bloque
/// via <see cref="Monitor"/> jusqu'à ce que le dernier run en cours soit relâché — donc testable de
/// façon déterministe avec un délai (« timer ») abstrait passé par l'appelant.
/// </para>
/// </summary>
public sealed class GracefulRunGate
{
    private readonly object _sync = new object();
    private int _activeRuns;
    private bool _shutdownRequested;

    /// <summary>Indique si un arrêt a été demandé.</summary>
    public bool IsShutdownRequested
    {
        get
        {
            lock (_sync)
            {
                return _shutdownRequested;
            }
        }
    }

    /// <summary>
    /// Ouvre un run si aucun arrêt n'est demandé. Renvoie un jeton à libérer en fin de run, ou
    /// <c>null</c> si l'arrêt est en cours (le run ne doit pas démarrer).
    /// </summary>
    public IDisposable? TryBeginRun()
    {
        lock (_sync)
        {
            if (_shutdownRequested)
            {
                return null;
            }

            _activeRuns++;
            return new RunScope(this);
        }
    }

    /// <summary>Demande l'arrêt : à partir d'ici, <see cref="TryBeginRun"/> renvoie <c>null</c>.</summary>
    public void RequestShutdown()
    {
        lock (_sync)
        {
            _shutdownRequested = true;
        }
    }

    /// <summary>Attend qu'aucun run ne soit en cours, au plus <paramref name="timeout"/>. <c>true</c> si la file est au repos.</summary>
    public bool WaitForIdle(TimeSpan timeout)
    {
        long totalMs = (long)timeout.TotalMilliseconds;
        if (totalMs < 0)
        {
            totalMs = 0;
        }

        lock (_sync)
        {
            long deadline = unchecked(Environment.TickCount + totalMs);
            while (_activeRuns != 0)
            {
                int remaining = unchecked((int)(deadline - Environment.TickCount));
                if (remaining <= 0)
                {
                    return false;
                }

                Monitor.Wait(_sync, remaining);
            }

            return true;
        }
    }

    private void EndRun()
    {
        lock (_sync)
        {
            _activeRuns--;
            if (_activeRuns == 0)
            {
                Monitor.PulseAll(_sync);
            }
        }
    }

    private sealed class RunScope : IDisposable
    {
        private readonly GracefulRunGate _gate;
        private int _disposed;

        public RunScope(GracefulRunGate gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _gate.EndRun();
            }
        }
    }
}
