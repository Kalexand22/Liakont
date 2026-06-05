namespace Liakont.Agent.Updater.Tests;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Core.Update;
using Xunit;

/// <summary>
/// Contrat JSON PARTAGÉ agent ↔ updater (ADR-0013) : l'updater (autonome) écrit le statut à la main,
/// l'agent le relit via <see cref="AutoUpdateStateStore"/>. Ce round-trip verrouille les noms de
/// propriété : une dérive casserait silencieusement le signalement de rollback/échec au heartbeat.
/// </summary>
public class UpdaterStatusContractTests
{
    [Fact]
    public void A_status_written_by_the_updater_is_read_back_by_the_agent_state_store()
    {
        string path = Path.Combine(Path.GetTempPath(), "liakont-updater-tests", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            UpdaterStatusWriter.Write(path, "2.0.0", new UpdaterResult(UpdaterOutcome.RolledBack, "Rollback effectué."));

            var store = new AutoUpdateStateStore(path);
            AutoUpdateStatus? status = store.TryGetLatest();

            status.Should().NotBeNull();
            status!.TargetVersion.Should().Be("2.0.0");
            status.Phase.Should().Be("RolledBack");
            status.Succeeded.Should().BeFalse();
            status.Message.Should().Be("Rollback effectué.");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
