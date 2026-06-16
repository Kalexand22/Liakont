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
    private const string DocumentApprovalContractsAssembly = "Liakont.Modules.DocumentApproval.Contracts";

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
        // SIG05 : l'acceptation 389 est PROJETÉE via le module générique DocumentApproval — la SEULE arête
        // cross-module autorisée est Mandats.Infrastructure → DocumentApproval.Contracts (jamais Domain/
        // Application/Infrastructure). C'est l'accès « par les Contracts » de module-rules §3 / CLAUDE.md n°14.
        ReferencedAssemblyNames(InfrastructureAssembly)
            .Should().NotContain(
                n => n.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                     && !n.StartsWith("Liakont.Modules.Mandats", StringComparison.Ordinal)
                     && n != DocumentApprovalContractsAssembly,
                "Mandats.Infrastructure ne référence aucun autre module métier hormis DocumentApproval.Contracts (SIG05, INV-MANDATS-2).");
    }

    [Fact]
    public void ModuleCsprojs_DoNotDeclareForbiddenProjectReferences()
    {
        var mandatsRoot = FindMandatsRoot();
        var mandatsRootSlash = mandatsRoot.Replace('\\', '/').TrimEnd('/');
        var srcRootSlash = Path.GetFullPath(Path.Combine(mandatsRoot, "..", ".."))
            .Replace('\\', '/').TrimEnd('/');

        // La frontière inter-modules est une garantie de PRODUCTION : on scanne les .csproj de production du
        // module (Domain/Application/Contracts/Infrastructure/Web), pas les projets de TEST. Un projet de test
        // compose légitimement l'infrastructure d'autres modules pour piloter ses scénarios (SIG05 :
        // Tests.Integration référence DocumentApproval.Infrastructure pour semer des validations) — sans pour
        // autant relâcher la frontière du runtime (gardée par Infrastructure_DoesNotReachIntoAnotherBusinessModule
        // au niveau ASSEMBLY + le scan des .csproj de production ci-dessous).
        var csprojs = Directory
            .EnumerateFiles(mandatsRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .Where(p => !p.Replace('\\', '/').Contains("/Tests."))
            .ToArray();

        csprojs.Should().NotBeEmpty("les .csproj de production du module Mandats doivent être localisables depuis le répertoire de test");

        // Références de projet autorisées : le module lui-même, le socle Common (Abstractions /
        // Infrastructure / Testing), le contrat agent partagé (BCL-only, autorisé pour tous les modules), et
        // le seam de planification du socle vendored `Stratum.Modules.Job.Contracts` (IJobHandler) — exigé par
        // le job SYSTÈME de bascule tacite (MND04, gabarit DailyAnchoring/SOL06), référencé de la même façon
        // par Archive/Supervision. C'est un module SOCLE (Stratum.Modules.Job), pas un module métier Liakont :
        // la frontière inter-modules (INV-MANDATS-2 ; test `Infrastructure_DoesNotReachIntoAnotherBusinessModule`)
        // reste verrouillée.
        bool IsAllowed(string resolvedSlash) =>
            resolvedSlash.StartsWith(mandatsRootSlash + "/", StringComparison.OrdinalIgnoreCase)
            || resolvedSlash.StartsWith(srcRootSlash + "/Common/", StringComparison.OrdinalIgnoreCase)
            || resolvedSlash.EndsWith(
                "/src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj",
                StringComparison.OrdinalIgnoreCase)
            || resolvedSlash.EndsWith(
                "/src/Modules/Job/Contracts/Stratum.Modules.Job.Contracts.csproj",
                StringComparison.OrdinalIgnoreCase)

            // SIG05 : projection self-billing via DocumentApproval — Contracts UNIQUEMENT (frontière module-rules §3).
            || resolvedSlash.EndsWith(
                "/src/Modules/DocumentApproval/Contracts/Liakont.Modules.DocumentApproval.Contracts.csproj",
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
            "un .csproj Mandats ne référence que le module lui-même, le socle Common, Liakont.Agent.Contracts, " +
            "le seam de planification socle Stratum.Modules.Job.Contracts (IJobHandler, MND04) et " +
            "DocumentApproval.Contracts (SIG05, projection self-billing) — jamais un autre module métier " +
            "ni le Domain/Application/Infrastructure d'un module (CLAUDE.md n°6/14, INV-MANDATS-2)");
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
