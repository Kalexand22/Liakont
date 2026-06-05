namespace Liakont.Agent.Cli.Tests.Commands;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Cli;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Cli.Tests;
using Xunit;

/// <summary>
/// Commande <c>test-api</c> (F12 §2.1, §3.3) : la sonde plateforme est injectée (doublure). OK → 0 ;
/// clé invalide/révoquée, URL injoignable ou clé non déchiffrable → « problème détecté ».
/// </summary>
public class TestApiCommandTests
{
    private const string Config = @"{ ""platformUrl"": ""https://liakont.editeur-x.fr"", ""apiKey"": ""APIKEY_FICTIVE"", ""extraction"": { ""adapter"": ""EncheresV6"" } }";

    [Fact]
    public void Ok_status_returns_ok()
    {
        int code = Execute((_, _) => new PlatformProbeResult(PlatformProbeStatus.Ok, "Plateforme joignable."), out string text);

        code.Should().Be(CliExitCode.Ok);
        text.Should().Contain("[OK]");
    }

    [Fact]
    public void Every_non_ok_status_returns_problem_detected()
    {
        // L'enum de diagnostic reste interne au CLI : on itère en interne plutôt que de l'exposer
        // dans la signature publique d'un [Theory] (CS0051).
        PlatformProbeStatus[] nonOk =
        {
            PlatformProbeStatus.InvalidKey,
            PlatformProbeStatus.RevokedKey,
            PlatformProbeStatus.Unreachable,
            PlatformProbeStatus.UpgradeRequired,
            PlatformProbeStatus.UnexpectedResponse,
        };

        foreach (PlatformProbeStatus status in nonOk)
        {
            int code = Execute((_, _) => new PlatformProbeResult(status, "diagnostic"), out string text);

            code.Should().Be(CliExitCode.ProblemDetected, "le statut {0} est un problème", status);
            text.Should().Contain("[ÉCHEC]");
        }
    }

    [Fact]
    public void Decrypted_key_and_url_are_passed_to_the_probe()
    {
        string? url = null;
        string? key = null;
        Execute(
            (u, k) =>
            {
                url = u;
                key = k;
                return new PlatformProbeResult(PlatformProbeStatus.Ok, "ok");
            },
            out _);

        url.Should().Be("https://liakont.editeur-x.fr");
        key.Should().Be("APIKEY_FICTIVE");
    }

    [Fact]
    public void Undecryptable_key_returns_problem_detected()
    {
        using var file = TempFile.WithContent(Config);
        var protector = new FakeSecretProtector();
        protector.MarkUndecryptable("APIKEY_FICTIVE");
        var command = new TestApiCommand(file.Path, protector, (_, _) => new PlatformProbeResult(PlatformProbeStatus.Ok, "ok"));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
        output.ToString().Should().Contain("non déchiffrable");
    }

    private static int Execute(Func<string, string, PlatformProbeResult> probe, out string text)
    {
        using var file = TempFile.WithContent(Config);
        var command = new TestApiCommand(file.Path, new FakeSecretProtector(), probe);
        using var output = new StringWriter();
        int code = command.Execute(Array.Empty<string>(), output);
        text = output.ToString();
        return code;
    }
}
