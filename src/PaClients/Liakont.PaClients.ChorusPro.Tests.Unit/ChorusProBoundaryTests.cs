namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière + encapsulation du plug-in Chorus Pro (acceptance CP02 ; blueprint.md §2 règle 4 ;
/// module-rules §6 ; CLAUDE.md n°6/14/16) : le plug-in ne référence QUE <c>Transmission.Contracts</c>
/// (+ Common), et AUCUN type propriétaire Chorus Pro (DTO « fil », client OAuth/PISTE) n'est exposé hors
/// de l'assembly. Vérifié au niveau IL (réflexion + NetArchTest) ET déclaratif (scan du .csproj).
/// </summary>
public sealed class ChorusProBoundaryTests
{
    private static readonly Assembly PluginAssembly = typeof(ChorusProClientFactory).Assembly;

    [Fact]
    public void Plugin_Does_Not_Reference_Any_Other_Pa_Plugin()
    {
        ReferencedAssemblyNames(PluginAssembly)
            .Should().NotContain(
                name => name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "un plug-in PA ne référence JAMAIS un autre plug-in (module-rules §6, CLAUDE.md n°6/16)");
    }

    [Fact]
    public void Plugin_Only_References_The_Transmission_Contracts_Module()
    {
        ReferencedAssemblyNames(PluginAssembly)
            .Should().NotContain(
                name => name.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                        && !string.Equals(name, "Liakont.Modules.Transmission.Contracts", StringComparison.Ordinal),
                "un plug-in ne référence que Transmission.Contracts (module-rules §6)");
    }

    [Fact]
    public void Plugin_Does_Not_Reach_Into_The_Transmission_Infrastructure()
    {
        var result = Types.InAssembly(PluginAssembly)
            .Should()
            .NotHaveDependencyOnAny("Liakont.Modules.Transmission.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "le plug-in n'accède au module que par ses Contracts — {0}",
            DescribeFailures(result));
    }

    [Fact]
    public void Plugin_Csproj_Only_Declares_Transmission_Contracts_As_ProjectReference()
    {
        var pluginRoot = FindPluginRoot();
        var csproj = Path.Combine(pluginRoot, "Liakont.PaClients.ChorusPro.csproj");
        File.Exists(csproj).Should().BeTrue("le .csproj du plug-in doit être localisable depuis le répertoire de test");

        const string allowedSuffix =
            "/src/Modules/Transmission/Contracts/Liakont.Modules.Transmission.Contracts.csproj";

        var resolvedRefs = XDocument.Load(csproj).Descendants("ProjectReference")
            .Attributes("Include").Select(a => a.Value)
            .Select(include => Path.GetFullPath(Path.Combine(pluginRoot, include)).Replace('\\', '/'))
            .ToArray();

        resolvedRefs.Should().NotBeEmpty("le plug-in référence au moins Transmission.Contracts");
        resolvedRefs.Should().OnlyContain(
            r => r.EndsWith(allowedSuffix, StringComparison.OrdinalIgnoreCase),
            "un plug-in PA ne déclare en ProjectReference que Transmission.Contracts (module-rules §6, CLAUDE.md n°6/16)");
    }

    [Fact]
    public void No_Proprietary_ChorusPro_Type_Is_Exposed_Outside_The_Assembly()
    {
        var exported = PluginAssembly.GetExportedTypes().Select(t => t.FullName ?? string.Empty).ToArray();

        exported.Should().NotContain(
            name => name.StartsWith("Liakont.PaClients.ChorusPro.Wire", StringComparison.Ordinal),
            "aucun DTO « fil » Chorus Pro (payload propriétaire) n'est exposé hors de l'assembly (acceptance CP02)");

        exported.Should().NotContain(
            "Liakont.PaClients.ChorusPro.ChorusProClient",
            "l'implémentation d'IPaClient reste interne, exposée seulement derrière l'abstraction");
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindPluginRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "PaClients", "Liakont.PaClients.ChorusPro");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
