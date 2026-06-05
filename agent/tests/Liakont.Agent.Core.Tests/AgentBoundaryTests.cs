namespace Liakont.Agent.Core.Tests;

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Contracts;
using Xunit;

/// <summary>
/// Vérifie la frontière agent/plateforme (blueprint.md §6, CLAUDE.md n°6) par liste BLANCHE (échec
/// fermé) : chaque assembly « bibliothèque » de l'agent ne référence QUE
/// <list type="bullet">
///   <item>le cadre .NET Framework / netstandard, identifié par son JETON DE CLÉ PUBLIQUE Microsoft
///   — pas par un simple préfixe « System. » qui laisserait passer une bibliothèque tierce homonyme ;</item>
///   <item>les assemblies <c>Liakont.Agent.*</c> ;</item>
///   <item>les dépendances tierces EXPLICITEMENT autorisées (sérialiseur Newtonsoft.Json, file locale
///   System.Data.SQLite — déclarées au catalogue central, blueprint.md §5 / F12 §3.4).</item>
/// </list>
/// Toute autre référence (Stratum.*, module ou plug-in PA de la plateforme, ou une nouvelle lib
/// tierce non déclarée — même nommée « System.X ») est une fuite. IExtractor (Liakont.Agent.Core)
/// est visible sans using : Core est un namespace englobant de Liakont.Agent.Core.Tests.
/// </summary>
public class AgentBoundaryTests
{
    // Jetons de clé publique des assemblies de référence Microsoft : clés de signature FIXES du
    // .NET Framework et de la façade netstandard (stables depuis des années). Identifier le cadre
    // par sa clé — et non par un préfixe de nom — ferme le trou « une lib tierce nommée System.X ».
    private static readonly string[] FrameworkPublicKeyTokens =
    {
        "b77a5c561934e089", // mscorlib, System, System.Core, System.Data, System.Xml...
        "b03f5f7f11d50a3a", // System.Security, Microsoft.CSharp...
        "31bf3856ad364e35", // WindowsBase et autres assemblies WPF/WCF du framework
        "cc7b13ffcd2ddd51", // netstandard (façade)
    };

    // Dépendances tierces explicitement autorisées de l'agent (Directory.Packages.props,
    // blueprint.md §5 / F12 §3.4). Nommées une à une pour que la garde reste FERMÉE.
    private static readonly string[] AllowedThirdPartyAssemblies =
    {
        "Newtonsoft.Json",
        "System.Data.SQLite",
    };

    [Fact]
    public void Agent_assemblies_reference_only_BCL_agent_and_declared_third_parties()
    {
        var agentAssemblies = new[]
        {
            typeof(AgentContractVersion).Assembly,
            typeof(IExtractor).Assembly,
            typeof(EncheresV6Extractor).Assembly,
        };

        var leaks = agentAssemblies
            .SelectMany(asm => asm.GetReferencedAssemblies().Select(r => new { Agent = asm.GetName().Name, Reference = r }))
            .Where(x => !IsAllowed(x.Reference))
            .Select(x => $"{x.Agent} -> {x.Reference.Name}")
            .ToArray();

        leaks.Should().BeEmpty("l'agent ne référence que le cadre Microsoft, Liakont.Agent.* et ses dépendances tierces déclarées (blueprint.md §6)");
    }

    private static bool IsAllowed(AssemblyName reference)
    {
        string? name = reference.Name;
        if (name == null)
        {
            return false;
        }

        if (name == "Liakont.Agent" || name.StartsWith("Liakont.Agent.", StringComparison.Ordinal))
        {
            return true;
        }

        if (AllowedThirdPartyAssemblies.Contains(name))
        {
            return true;
        }

        string token = TokenToString(reference.GetPublicKeyToken());
        return FrameworkPublicKeyTokens.Contains(token, StringComparer.OrdinalIgnoreCase);
    }

    private static string TokenToString(byte[]? token)
    {
        if (token == null || token.Length == 0)
        {
            return string.Empty;
        }

        return string.Concat(token.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
