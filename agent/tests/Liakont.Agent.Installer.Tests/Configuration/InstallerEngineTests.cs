namespace Liakont.Agent.Installer.Tests.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;
using Liakont.Agent.Installer.Tests.Fakes;
using Xunit;

/// <summary>
/// Garde du moteur d'installation « Core mocké » (F13 §3/§4) : déroulé complet (tests source/serveur,
/// installation avec agent.json chiffré), détection et refus d'un doublon d'instance, et isolation /
/// ciblage des instances (installer/désinstaller une instance sans toucher aux autres).
/// </summary>
public class InstallerEngineTests
{
    [Fact]
    public void Deroule_complet_installe_avec_un_agent_json_chiffre()
    {
        var deployer = new RecordingDeployer(true);
        InstallerEngine engine = EngineWith(new FakeInstanceCatalog("Default"), deployer);

        InstallationResult result = engine.Install(OpenProfile(), FullInput("ClientA"));

        result.Success.Should().BeTrue();
        deployer.InstalledPlans.Should().ContainSingle();
        deployer.InstalledPlans[0].InstanceName.Should().Be("ClientA");
        deployer.InstalledPlans[0].AgentJson.Should().Contain("ENC(");
        deployer.InstalledPlans[0].AgentJson.Should().NotContain("pk_clair_secret");
    }

    [Fact]
    public void Refuse_une_instance_deja_installee_sans_rien_deployer()
    {
        var deployer = new RecordingDeployer(true);
        InstallerEngine engine = EngineWith(new FakeInstanceCatalog("Default"), deployer);

        InstallationResult result = engine.Install(OpenProfile(), FullInput("Default"));

        result.Success.Should().BeFalse();
        result.Messages.Should().Contain(m => m.IndexOf("déjà installée", StringComparison.Ordinal) >= 0);
        deployer.InstalledPlans.Should().BeEmpty();
    }

    [Fact]
    public void Installe_une_nouvelle_instance_sans_toucher_aux_autres()
    {
        var deployer = new RecordingDeployer(true);
        InstallerEngine engine = EngineWith(new FakeInstanceCatalog("Default"), deployer);

        engine.Install(OpenProfile(), FullInput("ClientA"));

        deployer.InstalledPlans.Select(p => p.InstanceName).Should().Equal("ClientA");
    }

    [Fact]
    public void Valide_un_nom_neuf_et_refuse_un_doublon()
    {
        InstallerEngine engine = EngineWith(new FakeInstanceCatalog("ClientA"), new RecordingDeployer(true));

        engine.TryValidateNewInstanceName("ClientA", out _, out string? duplicateError).Should().BeFalse();
        duplicateError.Should().NotBeNull();
        duplicateError!.IndexOf("déjà installée", StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0);

        engine.TryValidateNewInstanceName("ClientB", out string name, out _).Should().BeTrue();
        name.Should().Be("ClientB");
    }

    [Fact]
    public void Refuse_un_nom_d_instance_invalide()
    {
        InstallerEngine engine = EngineWith(new FakeInstanceCatalog(), new RecordingDeployer(true));

        engine.TryValidateNewInstanceName("nom invalide !", out _, out string? error).Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void Desinstalle_une_instance_precise()
    {
        var deployer = new RecordingDeployer(true);
        InstallerEngine engine = EngineWith(new FakeInstanceCatalog("Default", "ClientA"), deployer);

        DeploymentOutcome outcome = engine.Uninstall("ClientA");

        outcome.Success.Should().BeTrue();
        deployer.UninstalledInstances.Should().Equal("ClientA");
    }

    [Fact]
    public void Teste_la_source_et_le_serveur_en_deleguant_aux_sondes()
    {
        var source = new FakeSourceProbe(new SourceTestResult(true, "5 tables détectées"));
        var platform = new FakePlatformProbe(new PlatformTestResult(false, "Clé API invalide (401)."));
        var engine = new InstallerEngine(source, platform, new FakeInstanceCatalog(), new RecordingDeployer(true), new FakeSecretProtector());

        engine.TestSource("DSN=Z").Message.Should().Be("5 tables détectées");
        source.LastConnectionString.Should().Be("DSN=Z");

        PlatformTestResult result = engine.TestPlatform("https://x.fr", "ma-cle");
        result.Success.Should().BeFalse();
        platform.LastUrl.Should().Be("https://x.fr");
        platform.LastApiKey.Should().Be("ma-cle");
    }

    private static InstallerEngine EngineWith(FakeInstanceCatalog catalog, RecordingDeployer deployer)
    {
        return new InstallerEngine(
            new FakeSourceProbe(new SourceTestResult(true, "ok")),
            new FakePlatformProbe(new PlatformTestResult(true, "ok")),
            catalog,
            deployer,
            new FakeSecretProtector());
    }

    private static IntegratorProfile OpenProfile()
    {
        return new IntegratorProfile(
            "test",
            IntegratorBranding.Empty,
            new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal));
    }

    private static InstallationInput FullInput(string instanceName)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ProfileFieldKeys.Adapter] = "EncheresV6",
            [ProfileFieldKeys.PlatformUrl] = "https://liakont.exemple.fr",
            [ProfileFieldKeys.ApiKey] = "pk_clair_secret",
            [ProfileFieldKeys.OdbcConnection] = "DSN=Source",
            [ProfileFieldKeys.Schedule] = "03:00",
            [ProfileFieldKeys.InstanceName] = instanceName,
        };
        return new InstallationInput(dict);
    }
}
