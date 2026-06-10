namespace Liakont.Agent.Core.Tests;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Core;
using Xunit;

/// <summary>
/// Chemins de l'agent dérivés de l'instance du processus (multi-instances, OPS05 pt 5).
/// AgentPaths porte un état statique de processus : chaque test restaure l'état initial
/// (<c>ResetForTesting</c>, InternalsVisibleTo) — les tests de cette classe s'exécutent
/// séquentiellement (même collection xUnit) et aucun autre test ne lit AgentPaths.
/// </summary>
public class AgentPathsTests : IDisposable
{
    public void Dispose()
    {
        AgentPaths.ResetForTesting();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Uninitialized_process_serves_the_default_instance_with_historical_paths()
    {
        string expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Liakont");

        AgentPaths.Current.IsDefault.Should().BeTrue();
        AgentPaths.RootDirectory.Should().Be(expectedRoot);
        AgentPaths.ConfigPath.Should().Be(Path.Combine(expectedRoot, "agent.json"));
        AgentPaths.DatabasePath.Should().Be(Path.Combine(expectedRoot, "agent-queue.db"));
        AgentPaths.LogDirectory.Should().Be(Path.Combine(expectedRoot, "logs"));
        AgentPaths.HeartbeatMarkerPath.Should().Be(Path.Combine(expectedRoot, "heartbeat.marker"));
    }

    [Fact]
    public void Initializing_a_named_instance_moves_every_path_under_its_directory()
    {
        AgentInstance.TryParse("AZMUT-01", out AgentInstance instance, out _).Should().BeTrue();
        string expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Liakont", "AZMUT-01");

        AgentPaths.Initialize(instance);

        AgentPaths.Current.Name.Should().Be("AZMUT-01");
        AgentPaths.RootDirectory.Should().Be(expectedRoot);
        AgentPaths.ConfigPath.Should().Be(Path.Combine(expectedRoot, "agent.json"));
        AgentPaths.DatabasePath.Should().Be(Path.Combine(expectedRoot, "agent-queue.db"));
        AgentPaths.LogDirectory.Should().Be(Path.Combine(expectedRoot, "logs"));
        AgentPaths.UpdateWorkDirectory.Should().Be(Path.Combine(expectedRoot, "update-work"));
        AgentPaths.UpdateStatusPath.Should().Be(Path.Combine(expectedRoot, "update-status.json"));
        AgentPaths.UpdateSigningKeyPath.Should().Be(Path.Combine(expectedRoot, "update-signing.pubkey.xml"));
        AgentPaths.HeartbeatMarkerPath.Should().Be(Path.Combine(expectedRoot, "heartbeat.marker"));
    }

    [Fact]
    public void Reinitializing_with_the_same_instance_is_idempotent()
    {
        AgentInstance.TryParse("ClientA", out AgentInstance first, out _).Should().BeTrue();
        AgentInstance.TryParse("clienta", out AgentInstance sameOtherCase, out _).Should().BeTrue();

        AgentPaths.Initialize(first);
        Action again = () => AgentPaths.Initialize(sameOtherCase);

        again.Should().NotThrow();
        AgentPaths.Current.Name.Should().Be("ClientA");
    }

    [Fact]
    public void Switching_instance_mid_process_throws()
    {
        AgentInstance.TryParse("ClientA", out AgentInstance first, out _).Should().BeTrue();
        AgentInstance.TryParse("ClientB", out AgentInstance second, out _).Should().BeTrue();

        AgentPaths.Initialize(first);
        Action switching = () => AgentPaths.Initialize(second);

        switching.Should().Throw<InvalidOperationException>()
            .WithMessage("*ClientA*ClientB*");
    }

    [Fact]
    public void Initialize_rejects_null()
    {
        Action call = () => AgentPaths.Initialize(null!);

        call.Should().Throw<ArgumentNullException>();
    }
}
