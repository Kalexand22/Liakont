namespace Liakont.Agent.Cli.Tests.Commands;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Cli;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Contracts;
using Xunit;

/// <summary>Commande <c>version</c> (F12 §2.1) : affiche la version de l'agent et celle du contrat.</summary>
public class VersionCommandTests
{
    [Fact]
    public void Prints_agent_and_contract_version_and_returns_ok()
    {
        var command = new VersionCommand();
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        string text = output.ToString();
        text.Should().Contain("Agent Liakont");
        text.Should().Contain(AgentContractVersion.ContractVersion);
        text.Should().Contain(AgentContractVersion.Current);
    }
}
