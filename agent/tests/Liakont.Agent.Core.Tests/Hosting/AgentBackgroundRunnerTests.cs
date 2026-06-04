namespace Liakont.Agent.Core.Tests.Hosting;

using System;
using System.Threading;
using FluentAssertions;
using Liakont.Agent.Core.Hosting;
using Xunit;

/// <summary>
/// Hôte de fond (AGT01) : l'arrêt ATTEND la fin du run en cours avant de rendre la main (F12 §2.2,
/// « un run d'extraction en cours se termine avant l'arrêt »). Testé avec un cycle injecté et des
/// délais (« timer ») abstraits.
/// </summary>
public class AgentBackgroundRunnerTests
{
    [Fact]
    public void Stop_waits_for_the_in_flight_run_to_complete()
    {
        using (var started = new ManualResetEventSlim(false))
        {
            int runs = 0;
            bool completed = false;

            void Cycle(CancellationToken token)
            {
                if (Interlocked.Increment(ref runs) == 1)
                {
                    started.Set();
                    Thread.Sleep(300); // run « long » en cours au moment du Stop
                    completed = true;
                }
            }

            // Intervalle long : un seul cycle s'exécute avant l'arrêt.
            var runner = new AgentBackgroundRunner(Cycle, TimeSpan.FromSeconds(30), new NullAgentLog());
            try
            {
                runner.Start();
                started.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("le premier cycle a démarré");

                bool idle = runner.Stop(TimeSpan.FromSeconds(5));

                idle.Should().BeTrue("l'arrêt doit revenir au repos");
                completed.Should().BeTrue("l'arrêt a attendu la fin du run en cours");
            }
            finally
            {
                runner.Dispose();
            }
        }
    }

    [Fact]
    public void Cannot_start_twice()
    {
        var runner = new AgentBackgroundRunner(_ => { }, TimeSpan.FromSeconds(30), new NullAgentLog());
        try
        {
            runner.Start();
            Action secondStart = () => runner.Start();
            secondStart.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            runner.Stop(TimeSpan.FromSeconds(2));
            runner.Dispose();
        }
    }
}
