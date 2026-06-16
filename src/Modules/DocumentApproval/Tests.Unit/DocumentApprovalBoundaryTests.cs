namespace Liakont.Modules.DocumentApproval.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.DocumentApproval.Infrastructure;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière du module DocumentApproval (CLAUDE.md n°6/14 ; module-rules §3 ; ADR-0028 §9,
/// INV-APPROVAL-8). La SEULE arête cross-module autorisée est <c>DocumentApproval → Signature.Contracts</c>
/// (niveau de preuve) ; JAMAIS <c>Signature.Domain/.Application/.Infrastructure</c>, un plug-in, ni un autre
/// module métier. Réflexion sur les assemblies référencées + scan déclaratif des <c>.csproj</c>.
/// </summary>
public sealed class DocumentApprovalBoundaryTests
{
    private const string SignatureContractsAssembly = "Liakont.Modules.Signature.Contracts";

    private static readonly Assembly ContractsAssembly = typeof(IDocumentApprovalQueries).Assembly;
    private static readonly Assembly DomainAssembly = typeof(DocumentValidation).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(DocumentApprovalModuleRegistration).Assembly;

    [Fact]
    public void Contracts_HasNoPersistenceDependency()
    {
        var result = Types.InAssembly(ContractsAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Dapper",
                "Npgsql",
                "Liakont.Modules.DocumentApproval.Infrastructure",
                "Liakont.Modules.DocumentApproval.Domain",
                "Liakont.Modules.DocumentApproval.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "DocumentApproval.Contracts doit rester sans dépendance de persistance — {0}", DescribeFailures(result));
    }

    [Fact]
    public void Contracts_DoesNotReachIntoAnotherModule()
    {
        OtherModuleReferences(ContractsAssembly).Should().BeEmpty(
            "la surface publique ne référence aucun autre module (les niveaux de preuve sont exposés par leur nom)");
    }

    [Fact]
    public void Domain_OnlyCrossModuleEdge_Is_Signature_Contracts()
    {
        OtherModuleReferences(DomainAssembly).Should().OnlyContain(
            name => name == SignatureContractsAssembly,
            "ADR-0028 §9 : DocumentApproval → Signature.Contracts UNIQUEMENT (jamais Signature.Domain/.PlugIns ni un autre module)");
    }

    [Fact]
    public void Infrastructure_OnlyCrossModuleEdge_Is_Signature_Contracts()
    {
        OtherModuleReferences(InfrastructureAssembly).Should().OnlyContain(
            name => name == SignatureContractsAssembly,
            "ADR-0028 §9 : aucune dépendance vers un autre module métier hormis Signature.Contracts");
    }

    [Fact]
    public void No_Reference_To_A_Concrete_Signature_PlugIn_Or_Signature_Internals()
    {
        foreach (var assembly in new[] { ContractsAssembly, DomainAssembly, InfrastructureAssembly })
        {
            ReferencedAssemblyNames(assembly).Should().NotContain(
                name => name.StartsWith("Liakont.Modules.Signature.", StringComparison.Ordinal)
                    && name != SignatureContractsAssembly,
                "ni Signature.Domain/.Application/.Infrastructure ni un plug-in concret ne traversent la frontière (ADR-0028 §9)");
        }
    }

    [Fact]
    public void ModuleCsprojs_DoNotDeclareForbiddenProjectReferences()
    {
        var moduleRoot = FindModuleRoot();
        var moduleRootSlash = moduleRoot.Replace('\\', '/').TrimEnd('/');
        var srcRootSlash = Path.GetFullPath(Path.Combine(moduleRoot, "..", ".."))
            .Replace('\\', '/').TrimEnd('/');

        var csprojs = Directory
            .EnumerateFiles(moduleRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .ToArray();

        csprojs.Should().NotBeEmpty("les .csproj du module DocumentApproval doivent être localisables");

        // Références autorisées : le module lui-même, le socle Common (Abstractions/Infrastructure/Testing) et
        // la SEULE arête cross-module Signature.Contracts (ADR-0028 §9). Tout le reste est interdit.
        bool IsAllowed(string resolvedSlash) =>
            resolvedSlash.StartsWith(moduleRootSlash + "/", StringComparison.OrdinalIgnoreCase)
            || resolvedSlash.StartsWith(srcRootSlash + "/Common/", StringComparison.OrdinalIgnoreCase)
            || resolvedSlash.EndsWith(
                "/src/Modules/Signature/Contracts/Liakont.Modules.Signature.Contracts.csproj",
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
            "un .csproj DocumentApproval ne référence que le module lui-même, le socle Common et " +
            "Signature.Contracts (ADR-0028 §9, CLAUDE.md n°6/14)");
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static IEnumerable<string> OtherModuleReferences(Assembly assembly) =>
        ReferencedAssemblyNames(assembly)
            .Where(n => n.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                && !n.StartsWith("Liakont.Modules.DocumentApproval", StringComparison.Ordinal));

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful ? string.Empty : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindModuleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "Modules", "DocumentApproval");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
