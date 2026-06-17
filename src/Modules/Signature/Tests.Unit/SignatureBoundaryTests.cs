namespace Liakont.Modules.Signature.Tests.Unit;

using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière de l'abstraction de signature (ADR-0027 §1 ; INV-SIGPROV-8 ; CLAUDE.md n°6/14/16) :
/// aucun type HTTP ne traverse l'abstraction (NetArchTest), et le module ne référence AUCUN plug-in concret
/// ni un autre module métier (réflexion sur assemblies + scan déclaratif des .csproj). Ces gardes échouent
/// dès qu'une dépendance interdite est introduite — elles sont triviales aujourd'hui (aucun plug-in
/// n'existe encore : Yousign = SIG07, Wacom = SIG08) mais verrouillent la frontière « un plug-in ne
/// référence que Signature.Contracts + Common » pour les items suivants.
/// </summary>
public sealed class SignatureBoundaryTests
{
    private static readonly Assembly ContractsAssembly = typeof(ISignatureProvider).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(SignatureProviderRegistry).Assembly;

    [Fact]
    public void Abstraction_HasNoHttpType()
    {
        // INV-SIGPROV-8 : aucun type HTTP dans l'abstraction (le payload propre au fournisseur vit dans
        // le plug-in). NetArchTest échouerait si un type exposait HttpClient/HttpRequestMessage…
        var result = Types.InAssembly(ContractsAssembly)
            .Should()
            .NotHaveDependencyOnAny("System.Net.Http", "System.Net.Sockets", "System.Net.HttpListener")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "l'abstraction de signature ne doit exposer aucun type HTTP — {0}",
            DescribeFailures(result));
    }

    [Fact]
    public void Contracts_DoesNotReferenceAnyConcreteSignaturePlugin()
    {
        // Un plug-in concret de signature (Yousign/Wacom) ne sera jamais référencé par le module générique
        // (CLAUDE.md n°6/16) — couvre les conventions de nommage plausibles des futurs plug-ins.
        ReferencedAssemblyNames(ContractsAssembly)
            .Should().NotContain(
                name => name.StartsWith("Liakont.SignatureProviders", StringComparison.Ordinal)
                    || name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "le module Signature ne référence JAMAIS un plug-in concret (CLAUDE.md n°6/16)");
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceAnyConcreteSignaturePlugin()
    {
        ReferencedAssemblyNames(InfrastructureAssembly)
            .Should().NotContain(
                name => name.StartsWith("Liakont.SignatureProviders", StringComparison.Ordinal)
                    || name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "le registre résout par clé, sans connaître aucun fournisseur concret (CLAUDE.md n°16)");
    }

    [Fact]
    public void Contracts_DoesNotReachIntoAnotherBusinessModule()
    {
        // Aucune référence à un AUTRE module métier (module-rules §3, CLAUDE.md n°14). L'abstraction est
        // BCL-only. NotContain reste significatif même sur un ensemble qui ne référence aucun Liakont.Modules.*.
        ReferencedAssemblyNames(ContractsAssembly)
            .Should().NotContain(
                n => n.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                     && !n.StartsWith("Liakont.Modules.Signature", StringComparison.Ordinal),
                "module-rules §3, CLAUDE.md n°14");
    }

    [Fact]
    public void ModuleCsprojs_OnlyReferenceTheSignatureModuleContractsOfOthersOrCommon()
    {
        // Complète les gardes IL par un scan DÉCLARATIF des .csproj : une ProjectReference interdite
        // déclarée mais non exercée par du code échoue ici (même principe qu'AgentProjectReferenceTests).
        // FRONTIÈRE (module-rules §3 ; CLAUDE.md n°6/14) : un .csproj Signature ne référence QUE
        //   (a) le module Signature lui-même (intra-module), OU
        //   (b) les *.Contracts d'un AUTRE module (jamais son Domain/Application/Infrastructure/Web), OU
        //   (c) le socle partagé Stratum.Common.* (src/Common/...).
        // SIG08 a élargi la garde de « BCL-only » (état SIG03) à « Contracts-only cross-module » : le proxy
        // OnSiteCapture consomme Documents.Contracts (re-vérif document↔company), SupportTrace.Contracts
        // (octets de l'artefact scellé) et Archive.Contracts (coffre WORM) — TOUS par leurs Contracts.
        // Atteindre le Domain/Application/Infrastructure d'un autre module, ou un plug-in concret, échoue ici.
        var signatureRoot = FindSignatureRoot();
        var signatureRootSlash = signatureRoot.Replace('\\', '/').TrimEnd('/');

        var csprojs = Directory
            .EnumerateFiles(signatureRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .ToArray();

        csprojs.Should().NotBeEmpty(
            "les .csproj du module Signature doivent être localisables depuis le répertoire de test");

        bool IsAllowed(string resolvedSlash)
        {
            // (a) intra-module : le module Signature lui-même.
            if (resolvedSlash.StartsWith(signatureRootSlash + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // (b) Contracts d'un AUTRE module UNIQUEMENT (frontière Contracts-only).
            if (resolvedSlash.Contains("/Modules/", StringComparison.OrdinalIgnoreCase)
                && resolvedSlash.Contains("/Contracts/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // (c) socle partagé Stratum.Common.*.
            return resolvedSlash.Contains("/Common/", StringComparison.OrdinalIgnoreCase);
        }

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
            "un .csproj Signature ne référence que lui-même, les *.Contracts d'autres modules, ou Stratum.Common.* "
            + "(jamais le Domain/Application/Infrastructure d'un autre module ni un plug-in concret — CLAUDE.md n°6/14/16)");
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindSignatureRoot()
    {
        // Remonte depuis le répertoire d'exécution des tests jusqu'à src/Liakont.sln, puis descend dans
        // src/Modules/Signature — même stratégie que TransmissionBoundaryTests.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "Modules", "Signature");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
