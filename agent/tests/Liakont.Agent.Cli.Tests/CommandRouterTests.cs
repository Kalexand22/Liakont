namespace Liakont.Agent.Cli.Tests;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Cli;
using Xunit;

/// <summary>
/// Aiguillage des commandes (F12 §2.1) : aide sans argument, code d'erreur sur commande inconnue,
/// dispatch correct et transmission des arguments (acceptation AGT05 « parsing des arguments »).
/// </summary>
public class CommandRouterTests
{
    [Fact]
    public void No_argument_prints_usage_and_returns_ok()
    {
        var router = new CommandRouter(new[] { new FakeCommand("dummy", CliExitCode.Ok) });
        using var output = new StringWriter();

        int code = router.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        output.ToString().Should().Contain("Commandes :").And.Contain("dummy");
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_aliases_print_usage_and_return_ok(string alias)
    {
        var router = new CommandRouter(new[] { new FakeCommand("dummy", CliExitCode.Ok) });
        using var output = new StringWriter();

        int code = router.Execute(new[] { alias }, output);

        code.Should().Be(CliExitCode.Ok);
        output.ToString().Should().Contain("Usage");
    }

    [Fact]
    public void Unknown_command_returns_execution_error()
    {
        var router = new CommandRouter(new[] { new FakeCommand("dummy", CliExitCode.Ok) });
        using var output = new StringWriter();
        string[] args = { "inconnue" };

        int code = router.Execute(args, output);

        code.Should().Be(CliExitCode.ExecutionError);
        output.ToString().Should().Contain("Commande inconnue");
    }

    [Fact]
    public void Known_command_is_dispatched_and_returns_its_exit_code()
    {
        var fake = new FakeCommand("diag", CliExitCode.ProblemDetected);
        var router = new CommandRouter(new[] { fake });
        using var output = new StringWriter();
        string[] args = { "diag" };

        int code = router.Execute(args, output);

        code.Should().Be(CliExitCode.ProblemDetected);
        fake.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public void Command_name_is_matched_case_insensitively()
    {
        var fake = new FakeCommand("diag", CliExitCode.Ok);
        var router = new CommandRouter(new[] { fake });
        using var output = new StringWriter();
        string[] args = { "DIAG" };

        int code = router.Execute(args, output);

        code.Should().Be(CliExitCode.Ok);
        fake.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public void Arguments_after_command_name_are_forwarded_without_the_name()
    {
        var fake = new FakeCommand("diag", CliExitCode.Ok);
        var router = new CommandRouter(new[] { fake });
        using var output = new StringWriter();
        string[] args = { "diag", "a", "b" };

        router.Execute(args, output);

        fake.ReceivedArgs.Should().Equal("a", "b");
    }
}
