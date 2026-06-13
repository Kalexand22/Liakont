namespace Liakont.Agent.Installer.Tests.Silent;

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;
using Liakont.Agent.Installer.Silent;
using Liakont.Agent.Installer.Tests.Fakes;
using Xunit;

/// <summary>
/// Garde du mode silencieux (F13 §3) : il pilote LE MÊME moteur que le wizard. Une installation valide
/// rend 0 et écrit le déroulé ; un champ requis manquant ou un profil invalide rend 1 SANS rien déployer
/// (bloquer plutôt qu'installer une configuration muette).
/// </summary>
public class SilentInstallerTests
{
    [Fact]
    public void Run_installe_rend_zero_et_ecrit_le_deroule()
    {
        var deployer = new RecordingDeployer(true);
        var silent = new SilentInstaller(EngineWith(deployer));
        using var writer = new StringWriter();

        int code = silent.Run(OpenProfile(), FullInput(), writer);

        code.Should().Be(0);
        deployer.InstalledPlans.Should().ContainSingle();
        writer.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public void Run_rend_un_quand_un_champ_requis_manque()
    {
        var deployer = new RecordingDeployer(true);
        var silent = new SilentInstaller(EngineWith(deployer));
        using var writer = new StringWriter();

        int code = silent.Run(OpenProfile(), InputWithoutApiKey(), writer);

        code.Should().Be(1);
        deployer.InstalledPlans.Should().BeEmpty();
    }

    [Fact]
    public void Run_rend_un_et_n_installe_rien_quand_le_profil_est_invalide()
    {
        var deployer = new RecordingDeployer(true);
        var silent = new SilentInstaller(EngineWith(deployer));
        using var writer = new StringWriter();

        // Profil invalide : un secret (apiKey) ne peut pas recevoir de valeur imposée (F13 §6).
        var declarations = new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal)
        {
            [ProfileFieldKeys.ApiKey] = new FieldDeclaration(FieldState.Shown, "pk_impose"),
        };
        var invalidProfile = new IntegratorProfile("invalide", IntegratorBranding.Empty, declarations);

        int code = silent.Run(invalidProfile, FullInput(), writer);

        code.Should().Be(1);
        deployer.InstalledPlans.Should().BeEmpty();
    }

    private static InstallerEngine EngineWith(RecordingDeployer deployer)
    {
        return new InstallerEngine(
            new FakeSourceProbe(new SourceTestResult(true, "ok")),
            new FakePlatformProbe(new PlatformTestResult(true, "ok")),
            new FakeInstanceCatalog(),
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

    private static InstallationInput FullInput()
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ProfileFieldKeys.Adapter] = "EncheresV6",
            [ProfileFieldKeys.PlatformUrl] = "https://liakont.exemple.fr",
            [ProfileFieldKeys.ApiKey] = "pk_clair",
            [ProfileFieldKeys.OdbcConnection] = "DSN=Source",
            [ProfileFieldKeys.InstanceName] = "ClientA",
        };
        return new InstallationInput(dict);
    }

    private static InstallationInput InputWithoutApiKey()
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ProfileFieldKeys.Adapter] = "EncheresV6",
            [ProfileFieldKeys.PlatformUrl] = "https://liakont.exemple.fr",
            [ProfileFieldKeys.InstanceName] = "ClientA",
        };
        return new InstallationInput(dict);
    }
}
