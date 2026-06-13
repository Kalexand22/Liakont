namespace Liakont.Modules.FleetSupervision.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Domain;
using Liakont.Modules.FleetSupervision.Infrastructure;
using Xunit;

/// <summary>
/// Gardes de frontière du module de méta-supervision de flotte (OPS04 ; module-rules §3 ; CLAUDE.md n°14) :
/// un module n'accède à un AUTRE module que par ses <c>Contracts</c>. Réflexion sur les assemblies
/// RÉFÉRENCÉES (niveau IL) + scan DÉCLARATIF des <c>.csproj</c> (une ProjectReference interdite déclarée mais
/// pas encore exercée échoue aussi). Le module ne dépend que de lui-même, du socle Common, et des Contracts
/// des modules Job (jobs système) et Notification (transport email).
/// </summary>
public sealed class FleetSupervisionBoundaryTests
{
    private static readonly Assembly ContractsAssembly = typeof(IFleetQueries).Assembly;
    private static readonly Assembly DomainAssembly = typeof(FleetInstance).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(FleetSupervisionOptions).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(FleetSupervisionModuleRegistration).Assembly;

    [Fact]
    public void Contracts_DoesNotReferenceAnyOtherModule()
    {
        ReferencedNames(ContractsAssembly)
            .Should().NotContain(n => IsForeignModule(n), "les Contracts de flotte ne référencent aucun autre module (module-rules §3)");
    }

    [Fact]
    public void Domain_DoesNotReferenceAnyOtherModule()
    {
        ReferencedNames(DomainAssembly).Should().NotContain(n => IsForeignModule(n), "module-rules §3");
    }

    [Fact]
    public void Application_DoesNotReferenceAnyOtherModule()
    {
        ReferencedNames(ApplicationAssembly).Should().NotContain(n => IsForeignModule(n), "module-rules §3");
    }

    [Fact]
    public void Infrastructure_ReferencesOnlyContractsOfOtherModules()
    {
        // L'Infrastructure peut référencer des modules du socle, mais UNIQUEMENT par leurs Contracts
        // (Job.Contracts, Notification.Contracts) — jamais leur Application/Infrastructure/Web.
        IEnumerable<string> foreignModuleRefs = ReferencedNames(InfrastructureAssembly)
            .Where(n => n.StartsWith("Stratum.Modules.", StringComparison.Ordinal)
                        || n.StartsWith("Liakont.Modules.", StringComparison.Ordinal))
            .Where(n => !n.StartsWith("Liakont.Modules.FleetSupervision", StringComparison.Ordinal));

        foreignModuleRefs.Should().OnlyContain(
            n => n.EndsWith(".Contracts", StringComparison.Ordinal),
            "un module n'accède à un autre module que par ses Contracts (module-rules §3, CLAUDE.md n°14)");
    }

    [Fact]
    public void ModuleCsprojs_DoNotDeclareForbiddenProjectReferences()
    {
        string moduleRoot = FindModuleRoot();
        string moduleRootSlash = moduleRoot.Replace('\\', '/').TrimEnd('/');

        string[] csprojs = Directory
            .EnumerateFiles(moduleRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal)
                        && !p.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
            .ToArray();

        csprojs.Should().NotBeEmpty("les .csproj du module doivent être localisables depuis le répertoire de test");

        var violations = (
            from csproj in csprojs
            let dir = Path.GetDirectoryName(csproj)!
            from include in XDocument.Load(csproj).Descendants("ProjectReference")
                .Attributes("Include").Select(a => a.Value)
            let resolved = Path.GetFullPath(Path.Combine(dir, include)).Replace('\\', '/')
            where !IsAllowedReference(resolved, moduleRootSlash)
            select $"{Path.GetFileName(csproj)} -> {include}")
            .ToArray();

        violations.Should().BeEmpty(
            "un .csproj du module ne référence que lui-même, le socle Common, et les Contracts des modules Job/Notification");
    }

    private static bool IsAllowedReference(string resolvedSlash, string moduleRootSlash)
    {
        if (resolvedSlash.StartsWith(moduleRootSlash + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] allowedSuffixes =
        [
            "/src/Common/Abstractions/Stratum.Common.Abstractions.csproj",
            "/src/Common/Infrastructure/Stratum.Common.Infrastructure.csproj",
            "/src/Common/Testing/Stratum.Common.Testing.csproj",
            "/src/Modules/Job/Contracts/Stratum.Modules.Job.Contracts.csproj",
            "/src/Modules/Notification/Contracts/Stratum.Modules.Notification.Contracts.csproj",
        ];

        return allowedSuffixes.Any(s => resolvedSlash.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsForeignModule(string assemblyName) =>
        (assemblyName.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
            && !assemblyName.StartsWith("Liakont.Modules.FleetSupervision", StringComparison.Ordinal))
        || assemblyName.StartsWith("Stratum.Modules.", StringComparison.Ordinal);

    private static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string FindModuleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "Modules", "FleetSupervision");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine du dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
