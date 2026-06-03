namespace Liakont.Agent.Core.Tests;

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

/// <summary>
/// Garde de frontiere agent/plateforme au niveau DECLARATIF (blueprint.md §6, CLAUDE.md n°6).
/// Complete AgentBoundaryTests (niveau IL via GetReferencedAssemblies(), qui ne voit que les
/// references reellement exercees) : ici on inspecte le graphe de ProjectReference des .csproj
/// de l'agent, donc une reference interdite DECLAREE mais non encore utilisee par du code
/// echoue quand meme (echec ferme). Seules sont autorisees les references internes a agent/ et
/// l'unique contrat partage src/Contracts/Liakont.Agent.Contracts. Le couplage par
/// PackageReference n'est pas un vecteur (la plateforme n'est pas publiee en paquet ; le vecteur
/// reel est une ProjectReference vers un projet plateforme).
/// </summary>
public class AgentProjectReferenceTests
{
    [Fact]
    public void No_agent_csproj_references_a_platform_project()
    {
        var agentRoot = FindAgentRoot();
        var agentRootSlash = agentRoot.Replace('\\', '/').TrimEnd('/');

        var csprojs = Directory
            .EnumerateFiles(agentRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .ToArray();

        csprojs.Should().NotBeEmpty("les .csproj de l'agent doivent etre localisables depuis le repertoire de test");

        bool IsAllowed(string resolvedSlash) =>
            resolvedSlash.StartsWith(agentRootSlash + "/", StringComparison.OrdinalIgnoreCase) ||
            resolvedSlash.EndsWith("/src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj", StringComparison.OrdinalIgnoreCase);

        var violations = (
            from csproj in csprojs
            let dir = Path.GetDirectoryName(csproj)!
            from include in XDocument.Load(csproj).Descendants("ProjectReference")
                .Attributes("Include").Select(a => a.Value)
            let resolved = Path.GetFullPath(Path.Combine(dir, include)).Replace('\\', '/')
            where !IsAllowed(resolved)
            select $"{Path.GetFileName(csproj)} -> {include}")
            .ToArray();

        violations.Should().BeEmpty("un .csproj agent ne reference que agent/ et src/Contracts/Liakont.Agent.Contracts (blueprint.md §6)");
    }

    private static string FindAgentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Liakont.Agent.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Racine agent/ (Liakont.Agent.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
