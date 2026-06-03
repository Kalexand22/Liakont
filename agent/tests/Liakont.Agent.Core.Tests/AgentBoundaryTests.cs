namespace Liakont.Agent.Core.Tests;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Contracts;
using Xunit;

/// <summary>
/// Vérifie la frontière agent/plateforme (blueprint.md §6, CLAUDE.md n°6) : aucun assembly
/// de l'agent ne référence le code plateforme (Stratum.*, Liakont.Host, modules, plug-ins PA).
/// IExtractor (Liakont.Agent.Core) est visible sans using : Core est un namespace englobant
/// de Liakont.Agent.Core.Tests.
/// </summary>
public class AgentBoundaryTests
{
    [Fact]
    public void No_agent_assembly_references_the_platform()
    {
        var agentAssemblies = new[]
        {
            typeof(AgentContractVersion).Assembly,
            typeof(IExtractor).Assembly,
            typeof(EncheresV6Extractor).Assembly,
        };

        bool IsPlatform(string? name) =>
            name != null &&
            (name.StartsWith("Stratum.", StringComparison.Ordinal) ||
             name.StartsWith("Liakont.Host", StringComparison.Ordinal) ||
             name.StartsWith("Liakont.Modules", StringComparison.Ordinal) ||
             name.StartsWith("Liakont.PaClients", StringComparison.Ordinal) ||
             name.StartsWith("Liakont.Common", StringComparison.Ordinal));

        var leaks = agentAssemblies
            .SelectMany(asm => asm.GetReferencedAssemblies().Select(r => new { Agent = asm.GetName().Name, Reference = r.Name }))
            .Where(x => IsPlatform(x.Reference))
            .Select(x => $"{x.Agent} -> {x.Reference}")
            .ToArray();

        leaks.Should().BeEmpty("l'agent ne référence jamais le code plateforme (blueprint.md §6)");
    }
}
