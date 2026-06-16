namespace Liakont.Modules.Mandats.Tests.Unit;

using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière du module Mandats (CLAUDE.md n°6/14 ; module-rules §3 ; INV-MANDATS-2) : le module
/// n'accède à AUCUN autre module métier, et sa surface publique (`Contracts`) reste consommable sans tirer
/// la persistance. Réflexion sur les assemblies référencées + scan déclaratif des `.csproj` (un
/// `ProjectReference` interdit déclaré mais non encore exercé par du code échoue ici). Ces gardes
/// verrouillent la frontière pour les items suivants du lot (MND02+).
/// </summary>
public sealed class MandatsBoundaryTests
{
    private static readonly Assembly ContractsAssembly = typeof(IMandatsQueries).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(MandatsModuleRegistration).Assembly;

    [Fact]
    public void Contracts_HasNoPersistenceDependency()
    {
        // La surface publique ne doit tirer ni Dapper ni Npgsql ni l'Infrastructure : un consommateur qui
        // référence Mandats.Contracts ne récupère pas la persistance (frontière propre).
        var result = Types.InAssembly(ContractsAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Dapper",
                "Npgsql",
                "Liakont.Modules.Mandats.Infrastructure",
                "Liakont.Modules.Mandats.Domain",
                "Liakont.Modules.Mandats.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Mandats.Contracts doit rester sans dépendance de persistance — {0}",
            DescribeFailures(result));
    }

    [Fact]
    public void Contracts_DoesNotReachIntoAnotherBusinessModule()
    {
        ReferencedAssemblyNames(ContractsAssembly)
            .Should().NotContain(
                n => n.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                     && !n.StartsWith("Liakont.Modules.Mandats", StringComparison.Ordinal),
                "module-rules §3, CLAUDE.md n°14 : un module n'accède à un autre que par ses Contracts.");
    }

    [Fact]
    public void Infrastructure_DoesNotReachIntoAnotherBusinessModule()
    {
        ReferencedAssemblyNames(InfrastructureAssembly)
            .Should().NotContain(
                n => n.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                     && !n.StartsWith("Liakont.Modules.Mandats", StringComparison.Ordinal),
                "MND01 (fondation) ne référence aucun autre module métier (INV-MANDATS-2).");
    }

    [Fact]
    public void ModuleCsprojs_DoNotDeclareForbiddenProjectReferences()
    {
        var mandatsRoot = FindMandatsRoot();
        var mandatsRootSlash = mandatsRoot.Replace('\\', '/').TrimEnd('/');
        var srcRootSlash = Path.GetFullPath(Path.Combine(mandatsRoot, "..", ".."))
            .Replace('\\', '/').TrimEnd('/');

        var csprojs = Directory
            .EnumerateFiles(mandatsRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .ToArray();

        csprojs.Should().NotBeEmpty("les .csproj du module Mandats doivent être localisables depuis le répertoire de test");

        // Références de projet autorisées : le module lui-même, le socle Common (Abstractions /
        // Infrastructure / Testing), et le contrat agent partagé (BCL-only, autorisé pour tous les modules).
        bool IsAllowed(string resolvedSlash) =>
            resolvedSlash.StartsWith(mandatsRootSlash + "/", StringComparison.OrdinalIgnoreCase)
            || resolvedSlash.StartsWith(srcRootSlash + "/Common/", StringComparison.OrdinalIgnoreCase)
            || resolvedSlash.EndsWith(
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
            "un .csproj Mandats ne référence que le module lui-même, le socle Common et Liakont.Agent.Contracts " +
            "(jamais un autre module métier — CLAUDE.md n°6/14, INV-MANDATS-2)");
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful ? string.Empty : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindMandatsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "Modules", "Mandats");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
