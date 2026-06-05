namespace Liakont.Agent.Core.Tests.Heartbeat;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Core.Heartbeat;
using Xunit;

/// <summary>
/// Sonde d'espace disque réelle (AGT03). Mesure sur un volume valide ; best-effort sur un chemin
/// invalide (jamais d'exception → <c>null</c>).
/// </summary>
public class DriveDiskFreeSpaceProbeTests
{
    [Fact]
    public void Measures_free_space_on_a_real_volume()
    {
        var probe = new DriveDiskFreeSpaceProbe(Path.GetTempPath());

        long? free = probe.GetAvailableFreeBytes();

        free.Should().NotBeNull();
        free!.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void An_invalid_path_is_best_effort_and_returns_null_without_throwing()
    {
        // Volume inexistant : la mesure échoue proprement, le heartbeat ne doit pas en pâtir.
        var probe = new DriveDiskFreeSpaceProbe(@"Z:\volume\inexistant\liakont\queue.db");

        Func<long?> act = () => probe.GetAvailableFreeBytes();

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void Requires_a_path()
    {
        Action act = () => _ = new DriveDiskFreeSpaceProbe("  ");
        act.Should().Throw<ArgumentException>();
    }
}
