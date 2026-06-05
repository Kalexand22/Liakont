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

    [Fact]
    public void Dispose_is_idempotent_and_stops_a_running_host()
    {
        var runner = new AgentBackgroundRunner(_ => { }, TimeSpan.FromSeconds(30), new NullAgentLog());
        runner.Start();

        Action disposeTwice = () =>
        {
            runner.Dispose();
            runner.Dispose(); // second appel : sans effet, sans exception
        };

        disposeTwice.Should().NotThrow();
    }

    [Fact]
    public void Dispose_while_a_cycle_overruns_the_grace_does_not_crash_the_process()
    {
        using (var inCycle = new ManualResetEventSlim(false))
        using (var release = new ManualResetEventSlim(false))
        using (var done = new ManualResetEventSlim(false))
        {
            bool started = false;

            void Cycle(CancellationToken token)
            {
                if (started)
                {
                    return;
                }

                started = true;
                inCycle.Set();

                // Ignore VOLONTAIREMENT le token (CancellationToken.None explicite) : on simule un
                // cycle qui dépasse la grâce sans honorer l'annulation, pour tester la course d'arrêt.
                release.Wait(CancellationToken.None);
                done.Set();
            }

            var runner = new AgentBackgroundRunner(Cycle, TimeSpan.FromSeconds(30), new NullAgentLog());
            runner.Start();
            inCycle.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

            runner.Stop(TimeSpan.FromMilliseconds(50)).Should().BeFalse("le cycle dépasse la grâce");
            runner.Dispose(); // libère le CTS alors que le thread de fond est encore vivant

            release.Set();    // le cycle se termine : la boucle touche ensuite un CTS disposé
            done.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue(
                "le cycle se termine et le thread sort proprement (pas d'ObjectDisposedException qui terminerait le process)");
        }
    }
}
