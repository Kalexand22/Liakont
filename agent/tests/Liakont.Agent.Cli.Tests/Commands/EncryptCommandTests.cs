namespace Liakont.Agent.Cli.Tests.Commands;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Cli;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Cli.Tests;
using Xunit;

/// <summary>
/// Commande <c>encrypt</c> (F12 §2.1) : chiffre une valeur passée en argument ou lue sur l'entrée
/// standard ; sans valeur, renvoie une erreur d'exécution avec un message d'usage.
/// </summary>
public class EncryptCommandTests
{
    [Fact]
    public void Encrypts_value_from_argument()
    {
        var command = new EncryptCommand(new FakeSecretProtector(), new StringReader(string.Empty));
        using var output = new StringWriter();
        string[] args = { "ma-cle-secrete" };

        int code = command.Execute(args, output);

        code.Should().Be(CliExitCode.Ok);
        output.ToString().Should().Contain("ENC(ma-cle-secrete)");
    }

    [Fact]
    public void Encrypts_value_from_standard_input_when_no_argument()
    {
        var command = new EncryptCommand(new FakeSecretProtector(), new StringReader("ma-cle-secrete"));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        output.ToString().Should().Contain("ENC(ma-cle-secrete)");
    }

    [Fact]
    public void Missing_value_returns_execution_error()
    {
        var command = new EncryptCommand(new FakeSecretProtector(), new StringReader(string.Empty));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ExecutionError);
        output.ToString().Should().Contain("Aucune valeur");
    }
}
