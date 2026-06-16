namespace Liakont.PaClients.Generique.Tests.Unit;

using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière du plug-in générique (F16 §6 ; blueprint.md §2 règle 4 ; module-rules §6 ;
/// CLAUDE.md n°6/14/16) : il ne référence QUE <c>Transmission.Contracts</c> (+ Common) — JAMAIS un autre
/// plug-in, JAMAIS un module métier (y compris FacturX et Notification), JAMAIS MailKit/MimeKit (la
/// composition MIME vit au Host derrière <c>IDocumentDeliveryChannel</c>). Et l'implémentation d'IPaClient
/// reste interne. Vérifié au niveau IL (réflexion + NetArchTest) ET déclaratif (scan du .csproj).
/// </summary>
public sealed class GeneriqueBoundaryTests
{
    private static readonly Assembly PluginAssembly = typeof(GeneriqueClientFactory).Assembly;

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
                "un plug-in ne référence que Transmission.Contracts — jamais FacturX, Notification, etc. (module-rules §6)");
    }

    [Fact]
    public void Plugin_Does_Not_Reference_Mail_Or_Notification_Libraries()
    {
        // La composition MIME (pièce jointe) est Host-only (MailKit/MimeKit) — le plug-in ne les voit pas (F16 §6.2).
        ReferencedAssemblyNames(PluginAssembly)
            .Should().NotContain(
                name => name.StartsWith("MailKit", StringComparison.Ordinal)
                        || name.StartsWith("MimeKit", StringComparison.Ordinal)
                        || name.Contains("Notification", StringComparison.Ordinal)
                        || name.Contains("FacturX", StringComparison.Ordinal),
                "le plug-in générique ne référence ni MailKit/MimeKit, ni Notification, ni FacturX (F16 §6.2)");
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
        var csproj = Path.Combine(pluginRoot, "Liakont.PaClients.Generique.csproj");
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
    public void Client_Implementation_Stays_Internal()
    {
        var exported = PluginAssembly.GetExportedTypes().Select(t => t.FullName ?? string.Empty).ToArray();

        exported.Should().NotContain(
            "Liakont.PaClients.Generique.GeneriqueClient",
            "l'implémentation d'IPaClient reste interne, exposée seulement derrière l'abstraction");
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful ? string.Empty : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindPluginRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "PaClients", "Liakont.PaClients.Generique");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
