namespace Liakont.Agent.Cli.Tests.Commands;

using System;
using System.IO;
using System.Threading;
using FluentAssertions;
using Liakont.Agent.Cli;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Core.Hosting;
using Xunit;

/// <summary>
/// Commande <c>run</c> (F12 §2.1) : déclenche un run manuel sous le MÊME verrou nommé que le run
/// planifié du service (acceptation AGT05 « verrou local partagé entre run manuel et run planifié »).
/// Les tests utilisent un mutex à nom LOCAL unique pour ne pas exiger SeCreateGlobalPrivilege ni
/// interférer entre tests — la sémantique du verrou partagé est identique à <c>Global\LiakontAgentRun</c>.
/// </summary>
public class RunCommandTests
{
    [Fact]
    public void Runs_cycle_when_lock_is_free_and_returns_ok()
    {
        bool ran = false;
        Func<TextWriter, bool> cycle = _ =>
        {
            ran = true;
            return true;
        };
        var command = new RunCommand(cycle, UniqueName(), TimeSpan.FromSeconds(1));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        ran.Should().BeTrue();
        output.ToString().Should().Contain("terminé");
    }

    [Fact]
    public void Refuses_and_does_not_run_when_a_run_is_already_in_progress()
    {
        string name = UniqueName();
        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        // Simule le run PLANIFIÉ détenant le verrou sur un autre thread (un mutex appartient au thread
        // qui l'acquiert) — exactement le cas « le service extrait pendant que l'intégrateur lance run ».
        InterProcessRunLock? held = null;
        var holder = new Thread(() =>
        {
            held = InterProcessRunLock.TryAcquire(TimeSpan.FromSeconds(1), name);
            acquired.Set();
            release.Wait();
            held?.Dispose();
        })
        {
            IsBackground = true,
        };
        holder.Start();
        acquired.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        held.Should().NotBeNull();

        bool ran = false;
        Func<TextWriter, bool> cycle = _ =>
        {
            ran = true;
            return true;
        };
        var command = new RunCommand(cycle, name, TimeSpan.FromMilliseconds(200));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
        ran.Should().BeFalse("le run manuel ne doit pas extraire pendant qu'un autre run détient le verrou");
        output.ToString().Should().Contain("déjà en cours");

        release.Set();
        holder.Join();
    }

    [Fact]
    public void Cycle_returning_false_returns_problem_detected()
    {
        var command = new RunCommand(_ => false, UniqueName(), TimeSpan.FromSeconds(1));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
    }

    [Fact]
    public void Cycle_throwing_returns_execution_error_and_releases_the_lock()
    {
        string name = UniqueName();
        var command = new RunCommand(_ => throw new InvalidOperationException("boom"), name, TimeSpan.FromSeconds(1));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ExecutionError);

        // Le verrou doit avoir été libéré malgré l'exception : un nouvel acquéreur l'obtient.
        InterProcessRunLock? after = InterProcessRunLock.TryAcquire(TimeSpan.FromSeconds(1), name);
        after.Should().NotBeNull();
        after!.Dispose();
    }

    private static string UniqueName() => $@"Local\LiakontCliRunTest_{Guid.NewGuid():N}";
}
