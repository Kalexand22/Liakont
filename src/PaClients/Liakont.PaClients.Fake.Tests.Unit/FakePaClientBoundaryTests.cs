namespace Liakont.PaClients.Fake.Tests.Unit;

using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière du plug-in factice (acceptance PAA02 ; blueprint.md §2 règle 4 ;
/// module-rules §6 ; CLAUDE.md n°6/16) : un plug-in PA ne référence QUE
/// <c>Transmission.Contracts</c> (+ Common) — jamais un autre plug-in, jamais un module métier,
/// jamais l'Infrastructure du module Transmission. Vérifié à la fois au niveau IL (réflexion +
/// NetArchTest) et au niveau déclaratif (scan du .csproj — une ProjectReference interdite déclarée
/// mais non exercée par du code échoue ici).
/// </summary>
public sealed class FakePaClientBoundaryTests
{
    private static readonly Assembly FakeAssembly = typeof(FakePaClient).Assembly;

    [Fact]
    public void Plugin_Does_Not_Reference_Any_Other_Pa_Plugin()
    {
        ReferencedAssemblyNames(FakeAssembly)
            .Should().NotContain(
                name => name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "un plug-in PA ne référence JAMAIS un autre plug-in (module-rules §6, CLAUDE.md n°6/16)");
    }

    [Fact]
    public void Plugin_Only_References_The_Transmission_Contracts_Module()
    {
        // Seule dépendance autorisée parmi les modules : Transmission.Contracts. Toute autre référence
        // Liakont.Modules.* (y compris Transmission.Infrastructure) est interdite — un plug-in n'accède
        // au module que par ses Contracts (module-rules §3/§6, CLAUDE.md n°14).
        ReferencedAssemblyNames(FakeAssembly)
            .Should().NotContain(
                name => name.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                        && !string.Equals(name, "Liakont.Modules.Transmission.Contracts", StringComparison.Ordinal),
                "un plug-in ne référence que Transmission.Contracts (module-rules §6)");
    }

    [Fact]
    public void Plugin_Does_Not_Reach_Into_The_Transmission_Infrastructure()
    {
        var result = Types.InAssembly(FakeAssembly)
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
        var csproj = Path.Combine(pluginRoot, "Liakont.PaClients.Fake.csproj");
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

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindPluginRoot()
    {
        // Remonte depuis le répertoire d'exécution des tests jusqu'à src/Liakont.sln, puis descend dans
        // src/PaClients/Liakont.PaClients.Fake — même stratégie que TransmissionBoundaryTests.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "PaClients", "Liakont.PaClients.Fake");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
