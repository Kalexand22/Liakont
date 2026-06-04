namespace Liakont.Agent.Core.Tests.Hosting;

using System;
using System.Threading;
using FluentAssertions;
using Liakont.Agent.Core.Hosting;
using Xunit;

/// <summary>
/// Barrière d'arrêt propre (AGT01) : un run en cours empêche le retour au repos ; après une demande
/// d'arrêt, aucun nouveau run ne démarre. Délais (« timer ») abstraits passés par le test.
/// </summary>
public class GracefulRunGateTests
{
    [Fact]
    public void Gate_is_idle_initially()
    {
        var gate = new GracefulRunGate();

        gate.WaitForIdle(TimeSpan.Zero).Should().BeTrue();
        gate.IsShutdownRequested.Should().BeFalse();
    }

    [Fact]
    public void Active_run_blocks_idle_until_released()
    {
        var gate = new GracefulRunGate();

        IDisposable? run = gate.TryBeginRun();
        run.Should().NotBeNull();
        gate.WaitForIdle(TimeSpan.FromMilliseconds(50)).Should().BeFalse("un run est en cours");

        run!.Dispose();
        gate.WaitForIdle(TimeSpan.FromSeconds(1)).Should().BeTrue("le run est terminé");
    }

    [Fact]
    public void After_shutdown_requested_no_new_run_starts()
    {
        var gate = new GracefulRunGate();

        gate.RequestShutdown();

        gate.IsShutdownRequested.Should().BeTrue();
        gate.TryBeginRun().Should().BeNull();
    }

    [Fact]
    public void Waiter_unblocks_when_in_flight_run_completes()
    {
        var gate = new GracefulRunGate();
        IDisposable run = gate.TryBeginRun()!;

        var releaser = new Thread(() =>
        {
            Thread.Sleep(150);
            run.Dispose();
        })
        {
            IsBackground = true,
        };
        releaser.Start();

        // Attente plus longue que la libération : doit revenir au repos quand le run se termine.
        gate.WaitForIdle(TimeSpan.FromSeconds(2)).Should().BeTrue();
        releaser.Join();
    }
}
