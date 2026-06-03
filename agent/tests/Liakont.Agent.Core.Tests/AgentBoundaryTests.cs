namespace Liakont.Agent.Core.Tests;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Contracts;
using Xunit;

/// <summary>
/// Vérifie la frontière agent/plateforme (blueprint.md §6, CLAUDE.md n°6) par liste BLANCHE
/// (échec fermé) : chaque assembly « bibliothèque » de l'agent ne référence QUE le BCL, les
/// assemblies Liakont.Agent.* et le sérialiseur de transport déclaré (Newtonsoft.Json). Toute
/// autre référence (Stratum.*, module ou plug-in PA de la plateforme, sous n'importe quel nom)
/// est une fuite. IExtractor (Liakont.Agent.Core) est visible sans using : Core est un namespace
/// englobant de Liakont.Agent.Core.Tests.
/// </summary>
public class AgentBoundaryTests
{
    [Fact]
    public void Agent_assemblies_reference_only_BCL_agent_and_the_declared_serializer()
    {
        var agentAssemblies = new[]
        {
            typeof(AgentContractVersion).Assembly,
            typeof(IExtractor).Assembly,
            typeof(EncheresV6Extractor).Assembly,
        };

        var bclPrefixes = new[] { "mscorlib", "netstandard", "System", "Microsoft.CSharp", "WindowsBase" };

        bool IsAllowed(string? name) =>
            name != null &&
            (bclPrefixes.Any(p => name == p || name.StartsWith(p + ".", StringComparison.Ordinal)) ||
             name == "Liakont.Agent" ||
             name.StartsWith("Liakont.Agent.", StringComparison.Ordinal) ||
             name == "Newtonsoft.Json");

        var leaks = agentAssemblies
            .SelectMany(asm => asm.GetReferencedAssemblies().Select(r => new { Agent = asm.GetName().Name, Reference = r.Name }))
            .Where(x => !IsAllowed(x.Reference))
            .Select(x => $"{x.Agent} -> {x.Reference}")
            .ToArray();

        leaks.Should().BeEmpty("l'agent ne référence que le BCL, Liakont.Agent.* et son sérialiseur (blueprint.md §6)");
    }
}
