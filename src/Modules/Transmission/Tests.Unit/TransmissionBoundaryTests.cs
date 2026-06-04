namespace Liakont.Modules.Transmission.Tests.Unit;

using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière de l'abstraction PA (acceptance PAA01 ; CLAUDE.md n°6/14/16 ;
/// INV-TRANSMISSION-005) : aucun type HTTP ne traverse l'abstraction (NetArchTest), et le module ne
/// référence AUCUNE PA concrète (plug-in) ni un autre module métier (réflexion sur assemblies
/// référencées + scan déclaratif des .csproj). Ces gardes échouent dès qu'une dépendance interdite
/// est introduite — c'est leur raison d'être (elles sont triviales aujourd'hui car aucun plug-in
/// n'existe encore, mais elles verrouillent la frontière pour les items suivants).
/// </summary>
public sealed class TransmissionBoundaryTests
{
    private static readonly Assembly ContractsAssembly = typeof(IPaClient).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(PaClientRegistry).Assembly;

    [Fact]
    public void Abstraction_HasNoHttpType()
    {
        // Aucun type HTTP dans l'abstraction (F05 §6 : la construction du payload PA-spécifique est
        // interne au plug-in). NetArchTest échouerait si un type exposait HttpClient/HttpRequestMessage…
        var result = Types.InAssembly(ContractsAssembly)
            .Should()
            .NotHaveDependencyOnAny("System.Net.Http", "System.Net.Sockets", "System.Net.HttpListener")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "l'abstraction PA ne doit exposer aucun type HTTP — {0}",
            DescribeFailures(result));
    }

    [Fact]
    public void Contracts_DoesNotReferenceAnyConcretePaPlugin()
    {
        ReferencedAssemblyNames(ContractsAssembly)
            .Should().NotContain(name => name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "le module Transmission ne référence JAMAIS un plug-in PA concret (CLAUDE.md n°6/16)");
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceAnyConcretePaPlugin()
    {
        ReferencedAssemblyNames(InfrastructureAssembly)
            .Should().NotContain(name => name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "le registre résout par clé, sans connaître aucune PA concrète (CLAUDE.md n°16)");
    }

    [Fact]
    public void Contracts_DoesNotReachIntoAnotherBusinessModule()
    {
        // Seule dépendance inter-projet autorisée : le contrat agent partagé (PivotDocumentDto). Aucune
        // référence à un AUTRE module métier (module-rules §3, CLAUDE.md n°14).
        // NOTE : .Where(...).OnlyContain(...) passe sur ensemble vide — on utilise NotContain pour que
        // l'assertion reste significative même quand ContractsAssembly ne référence aucun Liakont.Modules.*.
        ReferencedAssemblyNames(ContractsAssembly)
            .Should().NotContain(
                n => n.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                     && !n.StartsWith("Liakont.Modules.Transmission", StringComparison.Ordinal),
                "module-rules §3, CLAUDE.md n°14");
    }

    [Fact]
    public void ModuleCsprojs_DoNotDeclareForbiddenProjectReferences()
    {
        // Complète les gardes de réflexion (niveau IL) par un scan DÉCLARATIF des .csproj : une
        // ProjectReference interdite déclarée mais non encore exercée par du code échoue ici
        // (même principe qu'AgentProjectReferenceTests pour le sous-système agent).
        var transmissionRoot = FindTransmissionRoot();
        var transmissionRootSlash = transmissionRoot.Replace('\\', '/').TrimEnd('/');

        var csprojs = Directory
            .EnumerateFiles(transmissionRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .ToArray();

        csprojs.Should().NotBeEmpty(
            "les .csproj du module Transmission doivent être localisables depuis le répertoire de test");

        bool IsAllowed(string resolvedSlash) =>
            resolvedSlash.StartsWith(transmissionRootSlash + "/", StringComparison.OrdinalIgnoreCase) ||
            resolvedSlash.EndsWith(
                "/src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj",
                StringComparison.OrdinalIgnoreCase);

        var violations = (
            from csproj in csprojs
            let dir = Path.GetDirectoryName(csproj)!
            from include in XDocument.Load(csproj).Descendants("ProjectReference")
                .Attributes("Include").Select(a => a.Value)
            let resolved = Path.GetFullPath(Path.Combine(dir, include)).Replace('\\', '/')
            where !IsAllowed(resolved)
            select $"{Path.GetFileName(csproj)} -> {include}")
            .ToArray();

        violations.Should().BeEmpty(
            "un .csproj Transmission ne référence que le module lui-même et Liakont.Agent.Contracts (CLAUDE.md n°6/14/16)");
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindTransmissionRoot()
    {
        // Remonte depuis le répertoire d'exécution des tests jusqu'à src/Liakont.sln, puis descend
        // dans src/Modules/Transmission — même stratégie que AgentProjectReferenceTests.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "Modules", "Transmission");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
