namespace Liakont.Modules.SupportTrace.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Modules.SupportTrace.Contracts;
using Liakont.Modules.SupportTrace.Infrastructure;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière du module SupportTrace (FX06, F16 §7 ; CLAUDE.md n°4/14), sur le modèle de
/// <c>FacturXBoundaryTests</c>. L'invariant central (INV-SUPPORTTRACE-001) : la trace de support est un store
/// DISTINCT de la piste d'audit (<c>documents.document_events</c>, module Documents) et de l'archive probante
/// (coffre WORM, module Archive). Le module ne référence NI l'un NI l'autre → sa purge ne PEUT PAS les altérer
/// « par construction » (acceptance FX06 : « purgeable SANS toucher document_events ni l'archive probante »).
/// Deux niveaux : <b>NetArchTest</b> (dépendance IL) et <b>scan déclaratif des `.csproj`</b> (une dépendance
/// déclarée mais pas encore exercée échoue aussi — limite connue de l'analyse IL).
/// </summary>
public sealed class SupportTraceBoundaryTests
{
    private static readonly Assembly ContractsAssembly = typeof(ISupportTraceStore).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(FileSystemSupportTraceStore).Assembly;

    private static readonly Assembly[] AllLayers = [ContractsAssembly, InfrastructureAssembly];

    [Fact]
    public void SupportTrace_DoesNotDependOnTheAuditModule()
    {
        // INV-SUPPORTTRACE-001 : aucune dépendance vers le module Documents (qui détient document_events) :
        // la purge ne peut donc pas altérer la piste d'audit append-only (CLAUDE.md n°4).
        foreach (var assembly in AllLayers)
        {
            AssertNoDependencyOn(
                assembly,
                "Liakont.Modules.Documents",
                "SupportTrace ne référence pas le module Documents : la purge ne touche jamais document_events (FX06)");
        }
    }

    [Fact]
    public void SupportTrace_DoesNotDependOnTheProbativeArchiveModule()
    {
        // INV-SUPPORTTRACE-001 : aucune dépendance vers le module Archive (coffre WORM probant) : la purge ne
        // peut donc pas altérer l'archive probante (rétention 10 ans, Pilotage).
        foreach (var assembly in AllLayers)
        {
            AssertNoDependencyOn(
                assembly,
                "Liakont.Modules.Archive",
                "SupportTrace ne référence pas le module Archive : la purge ne touche jamais l'archive probante (FX06)");
        }
    }

    [Fact]
    public void SupportTrace_DoesNotReachIntoAnotherBusinessModule()
    {
        // module-rules §3 / CLAUDE.md n°14 : SupportTrace ne référence aucun AUTRE module métier. NetArchTest
        // ne sait pas exprimer « tout Liakont.Modules.* SAUF SupportTrace » → réflexion sur les assemblies
        // référencées (les seules dép inter-projet attendues sont le socle : Common.Abstractions, Job.Contracts).
        foreach (var assembly in AllLayers)
        {
            ReferencedAssemblyNames(assembly)
                .Should().NotContain(
                    name => name.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                            && !name.StartsWith("Liakont.Modules.SupportTrace", StringComparison.Ordinal),
                    "SupportTrace ne référence aucun autre module métier (module-rules §3) — {0}",
                    assembly.GetName().Name);
        }
    }

    [Fact]
    public void ModuleCsprojs_ForbidCrossModuleReferences()
    {
        var moduleRoot = FindModuleRoot();
        var moduleRootSlash = moduleRoot.Replace('\\', '/').TrimEnd('/');

        var csprojs = Directory
            .EnumerateFiles(moduleRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .ToArray();

        csprojs.Should().NotBeEmpty("les .csproj du module SupportTrace doivent être localisables depuis le test");

        // Seules ProjectReference autorisées : le module lui-même + les abstractions du socle (mécanique
        // multi-tenant) + les Contracts du module Job (handler système). JAMAIS Documents, Archive, ni un
        // autre module métier (CLAUDE.md n°14).
        bool IsAllowed(string resolvedSlash) =>
            resolvedSlash.StartsWith(moduleRootSlash + "/", StringComparison.OrdinalIgnoreCase) ||
            resolvedSlash.EndsWith("/src/Common/Abstractions/Stratum.Common.Abstractions.csproj", StringComparison.OrdinalIgnoreCase) ||
            resolvedSlash.EndsWith("/src/Modules/Job/Contracts/Stratum.Modules.Job.Contracts.csproj", StringComparison.OrdinalIgnoreCase);

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
            "un .csproj SupportTrace ne référence que le module, le socle (Common.Abstractions) et Job.Contracts (CLAUDE.md n°14)");
    }

    private static void AssertNoDependencyOn(Assembly assembly, string forbiddenNamespacePrefix, string because)
    {
        var result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(forbiddenNamespacePrefix)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "{0} — {1} ; types fautifs : {2}",
            because,
            assembly.GetName().Name,
            result.IsSuccessful ? string.Empty : string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string FindModuleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "Modules", "SupportTrace");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
