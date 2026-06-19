namespace Liakont.Modules.FacturX.Tests.Unit;

using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Modules.FacturX.Application;
using Liakont.Modules.FacturX.Contracts;
using Liakont.Modules.FacturX.Domain;
using Liakont.Modules.FacturX.Infrastructure;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière du module FacturX (acceptance FX02 ; ADR-0023 INV-FX-1/4 ; CLAUDE.md n°14),
/// sur le modèle de <c>TransmissionBoundaryTests</c>. Deux invariants verrouillés AVANT le code de
/// génération (FX03/FX04) : (1) QuestPDF est CONFINÉE à <c>FacturX.Infrastructure</c> — elle ne fuit
/// jamais vers Contracts/Domain/Application ; (2) la génération est INDÉPENDANTE de toute PA — FacturX
/// ne référence aucun plug-in PA, ni <c>Transmission.Contracts</c> (donc aucune <c>PaCapabilities</c>),
/// ni un autre module métier. Trois niveaux d'enforcement : <b>NetArchTest</b> (dépendance IL sur un
/// préfixe de namespace), <b>réflexion sur assemblies référencées</b> (contre-épreuve + cas « tout
/// autre module ») et <b>scan déclaratif des `.csproj`</b> (une dépendance déclarée mais pas encore
/// exercée par du code échoue aussi — limite connue de l'analyse IL).
/// </summary>
public sealed class FacturXBoundaryTests
{
    private static readonly Assembly ContractsAssembly = typeof(FacturXDocument).Assembly;
    private static readonly Assembly DomainAssembly = typeof(FacturXProfile).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IFacturXBuilder).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(FacturXModuleRegistration).Assembly;

    private static readonly Assembly[] UpperLayers = [ContractsAssembly, DomainAssembly, ApplicationAssembly];

    private static readonly Assembly[] AllLayers =
        [ContractsAssembly, DomainAssembly, ApplicationAssembly, InfrastructureAssembly];

    private static readonly string[] ExpectedQuestPdfDeclarers = ["Liakont.Modules.FacturX.Infrastructure"];

    [Fact]
    public void QuestPdf_IsNotReferencedAboveInfrastructure()
    {
        // INV-FX-1 : QuestPDF ne fuit pas vers Contracts/Domain/Application (NetArchTest, niveau IL).
        foreach (var assembly in UpperLayers)
        {
            AssertNoDependencyOn(
                assembly, "QuestPDF", "QuestPDF est confinée à FacturX.Infrastructure (ADR-0023 INV-FX-1)");
        }
    }

    [Fact]
    public void QuestPdf_IsReferencedByInfrastructure()
    {
        // Contre-épreuve : la dépendance EST là où elle doit être (sinon le confinement serait vide de sens).
        ReferencedAssemblyNames(InfrastructureAssembly)
            .Should().Contain(
                name => name.Equals("QuestPDF", StringComparison.Ordinal),
                "le scellement PDF/A-3 (FX04) vit dans FacturX.Infrastructure, seule couche à référencer QuestPDF (INV-FX-1)");
    }

    [Fact]
    public void FacturX_DoesNotReferenceAnyConcretePaPlugin()
    {
        // INV-FX-4 : aucune dépendance à un plug-in PA concret (Liakont.PaClients.*) — NetArchTest.
        foreach (var assembly in AllLayers)
        {
            AssertNoDependencyOn(
                assembly, "Liakont.PaClients", "FacturX ne référence aucun plug-in PA (ADR-0023 INV-FX-4)");
        }
    }

    [Fact]
    public void FacturX_DoesNotDependOnTransmissionNorPaCapabilities()
    {
        // INV-FX-4 : aucune dépendance à Transmission.Contracts — donc aucune consultation de PaCapabilities.
        foreach (var assembly in AllLayers)
        {
            AssertNoDependencyOn(
                assembly,
                "Liakont.Modules.Transmission",
                "FacturX ne dépend pas de Transmission.Contracts ni ne consulte PaCapabilities (ADR-0023 INV-FX-4)");
        }
    }

    [Fact]
    public void FacturX_DoesNotReachIntoAnotherBusinessModule()
    {
        // module-rules §3 / CLAUDE.md n°14 : un module n'accède à un autre que par ses Contracts ;
        // FacturX n'en référence AUCUN. NetArchTest ne sait pas exprimer « tout Liakont.Modules.* SAUF
        // FacturX » → réflexion sur les assemblies référencées (seule dép inter-projet : le contrat agent).
        foreach (var assembly in AllLayers)
        {
            ReferencedAssemblyNames(assembly)
                .Should().NotContain(
                    name => name.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                            && !name.StartsWith("Liakont.Modules.FacturX", StringComparison.Ordinal),
                    "FacturX ne référence aucun autre module métier (module-rules §3) — {0}",
                    assembly.GetName().Name);
        }
    }

    [Fact]
    public void ModuleCsprojs_ConfineQuestPdf_AndForbidCrossModuleReferences()
    {
        var moduleRoot = FindFacturXRoot();
        var moduleRootSlash = moduleRoot.Replace('\\', '/').TrimEnd('/');

        var csprojs = Directory
            .EnumerateFiles(moduleRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .ToArray();

        csprojs.Should().NotBeEmpty(
            "les .csproj du module FacturX doivent être localisables depuis le répertoire de test");

        // (a) INV-FX-1 : QuestPDF n'est déclarée QUE par FacturX.Infrastructure.
        var questPdfDeclarers = (
            from csproj in csprojs
            where XDocument.Load(csproj).Descendants("PackageReference")
                .Attributes("Include").Any(a => a.Value.Equals("QuestPDF", StringComparison.Ordinal))
            select Path.GetFileNameWithoutExtension(csproj))
            .ToArray();

        questPdfDeclarers.Should().BeEquivalentTo(
            ExpectedQuestPdfDeclarers,
            "QuestPDF n'est déclarée que par FacturX.Infrastructure (ADR-0023 INV-FX-1)");

        // (b) INV-FX-4 : aucune ProjectReference hors du module FacturX (sauf le contrat agent partagé).
        bool IsAllowed(string resolvedSlash) =>
            resolvedSlash.StartsWith(moduleRootSlash + "/", StringComparison.OrdinalIgnoreCase) ||
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
            "un .csproj FacturX ne référence que le module lui-même et Liakont.Agent.Contracts (ADR-0023 INV-FX-4)");
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
            DescribeFailures(result));
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful ? string.Empty : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindFacturXRoot()
    {
        // Remonte depuis le répertoire d'exécution des tests jusqu'à src/Liakont.sln, puis descend dans
        // src/Modules/FacturX — même stratégie que TransmissionBoundaryTests.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "Modules", "FacturX");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
