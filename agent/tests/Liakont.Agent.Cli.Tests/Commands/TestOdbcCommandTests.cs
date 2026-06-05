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
/// Commande <c>test-odbc</c> (F12 §2.1) : la sonde ODBC est injectée (doublure). Succès → liste des
/// tables + OK ; échec, chaîne absente ou non déchiffrable → « problème détecté ».
/// </summary>
public class TestOdbcCommandTests
{
    private const string ConfigWithOdbc = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""EncheresV6"", ""odbcConnectionString"": ""ODBC_FICTIVE"" } }";
    private const string ConfigWithoutOdbc = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""Fixture"" } }";

    [Fact]
    public void Successful_probe_lists_tables_and_returns_ok()
    {
        using var file = TempFile.WithContent(ConfigWithOdbc);
        string[] tables = { "FACTURES", "LIGNES" };
        var command = new TestOdbcCommand(file.Path, new FakeSecretProtector(), _ => OdbcProbeResult.Connected(tables));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        output.ToString().Should().Contain("2 table").And.Contain("FACTURES");
    }

    [Fact]
    public void Failed_probe_returns_problem_detected_with_message()
    {
        using var file = TempFile.WithContent(ConfigWithOdbc);
        var command = new TestOdbcCommand(file.Path, new FakeSecretProtector(), _ => OdbcProbeResult.Failed("Pilote ODBC absent."));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
        output.ToString().Should().Contain("Pilote ODBC absent");
    }

    [Fact]
    public void Decrypted_connection_string_is_passed_to_the_probe()
    {
        using var file = TempFile.WithContent(ConfigWithOdbc);
        string? seen = null;
        var command = new TestOdbcCommand(file.Path, new FakeSecretProtector(), cs =>
        {
            seen = cs;
            return OdbcProbeResult.Connected(Array.Empty<string>());
        });
        using var output = new StringWriter();

        command.Execute(Array.Empty<string>(), output);

        seen.Should().Be("ODBC_FICTIVE");
    }

    [Fact]
    public void Absent_odbc_returns_problem_detected()
    {
        using var file = TempFile.WithContent(ConfigWithoutOdbc);
        var command = new TestOdbcCommand(file.Path, new FakeSecretProtector(), _ => OdbcProbeResult.Connected(Array.Empty<string>()));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
        output.ToString().Should().Contain("Aucune chaîne ODBC");
    }

    [Fact]
    public void Undecryptable_odbc_returns_problem_detected()
    {
        using var file = TempFile.WithContent(ConfigWithOdbc);
        var protector = new FakeSecretProtector();
        protector.MarkUndecryptable("ODBC_FICTIVE");
        var command = new TestOdbcCommand(file.Path, protector, _ => OdbcProbeResult.Connected(Array.Empty<string>()));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
        output.ToString().Should().Contain("non déchiffrable");
    }
}
