namespace Liakont.Agent.Cli.Tests.Commands;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Cli;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Cli.Tests;
using Xunit;

/// <summary>
/// Commande <c>check-config</c> (F12 §2.1) : chaque point de contrôle (fichier, secrets déchiffrables,
/// adaptateur connu) est rapporté, et un échec quelconque renvoie « problème détecté ».
/// </summary>
public class CheckConfigCommandTests
{
    private const string ValidJson = @"{
  ""platformUrl"": ""https://liakont.editeur-x.fr"",
  ""apiKey"": ""APIKEY_FICTIVE"",
  ""extraction"": { ""adapter"": ""EncheresV6"", ""odbcConnectionString"": ""ODBC_FICTIVE"", ""schedule"": [""03:00""] }
}";

    private static readonly string[] KnownAdapters = { "EncheresV6" };

    [Fact]
    public void Valid_config_with_decryptable_secrets_and_known_adapter_returns_ok()
    {
        using var file = TempFile.WithContent(ValidJson);
        var command = new CheckConfigCommand(file.Path, new FakeSecretProtector(), KnownAdapters);
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        output.ToString().Should().NotContain("[ÉCHEC]");
    }

    [Fact]
    public void Missing_file_returns_problem_detected_with_french_message()
    {
        var command = new CheckConfigCommand(TempFile.NonExistentPath(), new FakeSecretProtector(), KnownAdapters);
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
        output.ToString().Should().Contain("introuvable");
    }

    [Fact]
    public void Undecryptable_api_key_is_reported_and_returns_problem_detected()
    {
        using var file = TempFile.WithContent(ValidJson);
        var protector = new FakeSecretProtector();
        protector.MarkUndecryptable("APIKEY_FICTIVE");
        var command = new CheckConfigCommand(file.Path, protector, KnownAdapters);
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
        output.ToString().Should().Contain("non déchiffrable");
    }

    [Fact]
    public void Unknown_adapter_is_reported_and_returns_problem_detected()
    {
        using var file = TempFile.WithContent(ValidJson);
        string[] otherAdapters = { "Sage" };
        var command = new CheckConfigCommand(file.Path, new FakeSecretProtector(), otherAdapters);
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ProblemDetected);
        output.ToString().Should().Contain("inconnu");
    }

    [Fact]
    public void Absent_odbc_is_informational_and_returns_ok()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""EncheresV6"" } }";
        using var file = TempFile.WithContent(json);
        var command = new CheckConfigCommand(file.Path, new FakeSecretProtector(), KnownAdapters);
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        output.ToString().Should().Contain("Aucune chaîne ODBC");
    }
}
