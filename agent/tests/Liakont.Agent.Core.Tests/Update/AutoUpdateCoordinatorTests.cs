namespace Liakont.Agent.Core.Tests.Update;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Update;
using Xunit;

/// <summary>
/// Coordinateur d'auto-update (AGT04, F12 §2.5, ADR-0013). Couvre : lancement nominal (signature +
/// hash valides), REFUS sur signature invalide, REFUS sur hash invalide, REFUS sans clé (fail-closed),
/// DIFFÉRÉ pendant un run, garde anti-downgrade, déclenchement par 426, et garde de ré-entrance.
/// </summary>
public class AutoUpdateCoordinatorTests
{
    private const string ManifestUrl = "https://updates.example/agent/manifest.json";
    private const string PackageUrl = "https://updates.example/agent/agent-2.0.0.zip";
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_valid_signed_update_with_a_matching_hash_launches_the_detached_updater()
    {
        using (var workspace = new TempDirectory())
        {
            var source = new FakeUpdatePackageSource();
            GivenValidSignedPackage(source, "2.0.0");
            var launcher = new FakeUpdaterLauncher();
            var store = NewStatusStore(workspace);
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, new StubManifestSignatureVerifier(), new FakeRunActivityProbe(), launcher, store, "1.0.0");

            AutoUpdateResult result = coordinator.ConsiderHeartbeatConfiguration(UpdateRequiredConfig());

            result.Outcome.Should().Be(AutoUpdateOutcome.Launched);
            source.RequestedManifestUrl.Should().Be(ManifestUrl);
            source.RequestedPackageUrl.Should().Be(PackageUrl);
            launcher.Captured.Should().NotBeNull();
            launcher.Captured!.TargetVersion.Should().Be("2.0.0");
            Directory.Exists(launcher.Captured.StagingDirectory).Should().BeTrue("le paquet vérifié est extrait avant le lancement");
            store.TryGetLatest()!.Succeeded.Should().BeFalse("le succès n'est confirmé que par l'updater, pas au lancement");
        }
    }

    [Fact]
    public void An_invalid_manifest_signature_is_refused_and_signalled()
    {
        using (var workspace = new TempDirectory())
        {
            var source = new FakeUpdatePackageSource();
            GivenValidSignedPackage(source, "2.0.0");
            var verifier = new StubManifestSignatureVerifier { SignatureValid = false };
            var launcher = new FakeUpdaterLauncher();
            var store = NewStatusStore(workspace);
            var log = new CapturingAgentLog();
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, verifier, new FakeRunActivityProbe(), launcher, store, "1.0.0", log);

            AutoUpdateResult result = coordinator.ConsiderHeartbeatConfiguration(UpdateRequiredConfig());

            result.Outcome.Should().Be(AutoUpdateOutcome.RejectedSignature);
            launcher.Captured.Should().BeNull("un manifeste non authentique ne doit JAMAIS lancer d'updater");
            log.Warnings.Should().Contain(w => w.Contains("signature"));
            store.TryGetLatest()!.Succeeded.Should().BeFalse();
        }
    }

    [Fact]
    public void An_invalid_package_hash_is_refused_and_signalled()
    {
        using (var workspace = new TempDirectory())
        {
            byte[] package = UpdateTestData.MakeZipPackage();

            // Empreinte annoncée VOLONTAIREMENT fausse (ne correspond pas au paquet servi).
            string wrongHash = new string('0', 64);
            var source = new FakeUpdatePackageSource
            {
                PackageBytes = package,
                ManifestBytes = UpdateTestData.ManifestBytes("2.0.0", PackageUrl, wrongHash),
            };
            var launcher = new FakeUpdaterLauncher();
            var store = NewStatusStore(workspace);
            var log = new CapturingAgentLog();
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, new StubManifestSignatureVerifier(), new FakeRunActivityProbe(), launcher, store, "1.0.0", log);

            AutoUpdateResult result = coordinator.ConsiderHeartbeatConfiguration(UpdateRequiredConfig());

            result.Outcome.Should().Be(AutoUpdateOutcome.RejectedHash);
            launcher.Captured.Should().BeNull();
            log.Warnings.Should().Contain(w => w.Contains("empreinte"));
            store.TryGetLatest()!.Succeeded.Should().BeFalse();
        }
    }

    [Fact]
    public void An_update_is_deferred_while_an_extraction_run_is_in_progress()
    {
        using (var workspace = new TempDirectory())
        {
            var source = new FakeUpdatePackageSource();
            GivenValidSignedPackage(source, "2.0.0");
            var launcher = new FakeUpdaterLauncher();
            var probe = new FakeRunActivityProbe { InProgress = true };
            var store = NewStatusStore(workspace);
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, new StubManifestSignatureVerifier(), probe, launcher, store, "1.0.0");

            AutoUpdateResult result = coordinator.ConsiderHeartbeatConfiguration(UpdateRequiredConfig());

            result.Outcome.Should().Be(AutoUpdateOutcome.DeferredRunInProgress);
            launcher.Captured.Should().BeNull("jamais d'update pendant un run d'extraction");
            source.RequestedManifestUrl.Should().BeNull("on n'a même pas téléchargé : on diffère avant");
        }
    }

    [Fact]
    public void A_missing_signing_key_refuses_the_update_fail_closed()
    {
        using (var workspace = new TempDirectory())
        {
            var source = new FakeUpdatePackageSource();
            GivenValidSignedPackage(source, "2.0.0");
            var verifier = new StubManifestSignatureVerifier { KeyPresent = false };
            var launcher = new FakeUpdaterLauncher();
            var store = NewStatusStore(workspace);
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, verifier, new FakeRunActivityProbe(), launcher, store, "1.0.0");

            AutoUpdateResult result = coordinator.ConsiderHeartbeatConfiguration(UpdateRequiredConfig());

            result.Outcome.Should().Be(AutoUpdateOutcome.MissingSigningKey);
            launcher.Captured.Should().BeNull();
        }
    }

    [Fact]
    public void A_version_not_newer_than_the_current_one_is_skipped()
    {
        using (var workspace = new TempDirectory())
        {
            var source = new FakeUpdatePackageSource();
            GivenValidSignedPackage(source, "1.0.0"); // identique à la version courante
            var launcher = new FakeUpdaterLauncher();
            var store = NewStatusStore(workspace);
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, new StubManifestSignatureVerifier(), new FakeRunActivityProbe(), launcher, store, "1.0.0");

            AutoUpdateResult result = coordinator.ConsiderHeartbeatConfiguration(UpdateRequiredConfig());

            result.Outcome.Should().Be(AutoUpdateOutcome.AlreadyCurrent);
            launcher.Captured.Should().BeNull("pas de downgrade ni de réinstallation de la même version");
        }
    }

    [Fact]
    public void No_update_is_attempted_when_none_is_requested()
    {
        using (var workspace = new TempDirectory())
        {
            var source = new FakeUpdatePackageSource();
            GivenValidSignedPackage(source, "2.0.0");
            var launcher = new FakeUpdaterLauncher();
            var store = NewStatusStore(workspace);
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, new StubManifestSignatureVerifier(), new FakeRunActivityProbe(), launcher, store, "1.0.0");

            var config = new AgentConfigurationDto(updateRequired: false, updateUrl: ManifestUrl, versionManifestSignature: "c2ln");
            AutoUpdateResult result = coordinator.ConsiderHeartbeatConfiguration(config);

            result.Outcome.Should().Be(AutoUpdateOutcome.NotRequested);
            source.RequestedManifestUrl.Should().BeNull();
            launcher.Captured.Should().BeNull();
        }
    }

    [Fact]
    public void A_426_push_signal_triggers_the_update_on_the_next_heartbeat()
    {
        using (var workspace = new TempDirectory())
        {
            var source = new FakeUpdatePackageSource();
            GivenValidSignedPackage(source, "2.0.0");
            var launcher = new FakeUpdaterLauncher();
            var store = NewStatusStore(workspace);
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, new StubManifestSignatureVerifier(), new FakeRunActivityProbe(), launcher, store, "1.0.0");

            // 426 reçu pendant un run : le service mémorise le besoin de mise à jour…
            coordinator.RecordPushUpgradeRequired();

            // …et au heartbeat suivant, même sans updateRequired explicite, la mise à jour part.
            var config = new AgentConfigurationDto(updateRequired: false, updateUrl: ManifestUrl, versionManifestSignature: "c2ln");
            AutoUpdateResult result = coordinator.ConsiderHeartbeatConfiguration(config);

            result.Outcome.Should().Be(AutoUpdateOutcome.Launched);
            launcher.Captured.Should().NotBeNull();
        }
    }

    [Fact]
    public void A_second_attempt_is_rejected_while_an_update_is_already_in_progress()
    {
        using (var workspace = new TempDirectory())
        {
            var source = new FakeUpdatePackageSource();
            GivenValidSignedPackage(source, "2.0.0");
            var launcher = new FakeUpdaterLauncher();
            var store = NewStatusStore(workspace);
            AutoUpdateCoordinator coordinator = BuildCoordinator(workspace, source, new StubManifestSignatureVerifier(), new FakeRunActivityProbe(), launcher, store, "1.0.0");

            coordinator.ConsiderHeartbeatConfiguration(UpdateRequiredConfig()).Outcome.Should().Be(AutoUpdateOutcome.Launched);
            AutoUpdateResult second = coordinator.ConsiderHeartbeatConfiguration(UpdateRequiredConfig());

            second.Outcome.Should().Be(AutoUpdateOutcome.AlreadyInProgress);
            launcher.LaunchCount.Should().Be(1, "aucun second updater ne doit partir tant que le premier remplace les binaires");
        }
    }

    private static AgentConfigurationDto UpdateRequiredConfig() =>
        new AgentConfigurationDto(updateRequired: true, updateUrl: ManifestUrl, versionManifestSignature: "c2ln");

    private static void GivenValidSignedPackage(FakeUpdatePackageSource source, string version)
    {
        byte[] package = UpdateTestData.MakeZipPackage();
        source.PackageBytes = package;
        source.ManifestBytes = UpdateTestData.ManifestBytes(version, PackageUrl, UpdateTestData.Sha256Hex(package));
    }

    private static AutoUpdateStateStore NewStatusStore(TempDirectory workspace) =>
        new AutoUpdateStateStore(workspace.Combine("update-status.json"));

    private static AutoUpdateCoordinator BuildCoordinator(
        TempDirectory workspace,
        FakeUpdatePackageSource source,
        StubManifestSignatureVerifier verifier,
        FakeRunActivityProbe probe,
        FakeUpdaterLauncher launcher,
        AutoUpdateStateStore store,
        string currentVersion,
        CapturingAgentLog? log = null)
    {
        var environment = new AutoUpdateEnvironment(
            currentVersion,
            "LiakontAgent",
            workspace.Combine("install"),
            workspace.Combine("work"),
            workspace.Combine("updater.log"),
            workspace.Combine("update-status.json"),
            workspace.Combine("heartbeat.marker"),
            TimeSpan.FromMinutes(5));

        return new AutoUpdateCoordinator(
            source,
            verifier,
            probe,
            launcher,
            store,
            environment,
            new MutableClock(Now),
            log ?? new CapturingAgentLog());
    }
}
