namespace Liakont.Modules.Ged.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Modules.Ged.Infrastructure;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière du module GED (F19 §7/§8, module-rules §3, CLAUDE.md n°14), à DEUX niveaux
/// complémentaires :
/// <list type="bullet">
/// <item><description>
/// <b>NetArchTest (IL)</b> : les assemblies de production GED ne portent AUCUNE dépendance de type vers le
/// flux fiscal (« Ged.Domain → aucune dépendance fiscale », F19 §7). Voit l'usage EFFECTIF des types.
/// </description></item>
/// <item><description>
/// <b>Scan déclaratif des <c>.csproj</c></b> (modèle <c>StratumPackagingBoundaryTests</c>) : couvre la
/// « référence sèche » (une <c>ProjectReference</c> ajoutée mais dont le type n'est pas encore utilisé) —
/// que l'IL ne peut pas voir — et reste significatif quand le module GED, au stade scaffold, ne référence
/// encore aucun autre module. C'est ce niveau qui prouve « flux fiscal ⇏ Ged » sans référencer les
/// assemblies fiscales depuis le projet de test.
/// </description></item>
/// </list>
/// </summary>
public sealed class GedBoundaryTests
{
    // Plancher de cohérence : sans projets à inspecter, une garde passerait à VIDE (faux vert). Le module
    // GED expose 5 couches de production (Contracts/Domain/Application/Infrastructure/Web) ; les 4 modules du
    // flux fiscal exposent au moins un .csproj de production chacun.
    private const int MinimumGedProductionProjects = 5;

    // Modules du FLUX FISCAL d'émission — liste EXPLICITE de F19 §7 (« ★ INTERDIT (P1) :
    // Pipeline/Validation/Transmission/Documents ──X──▶ Ged.* »). La garde est volontairement restreinte à
    // cet ensemble : le silo fiscal interdit à CES modules toute dépendance vers Ged. Qu'un module d'intake
    // (Ingestion, Staging, …) référence un jour la SURFACE PUBLIQUE Ged.Contracts est permis par design
    // (module-rules §3, accès inter-module par les Contracts) et n'est donc PAS une violation de frontière.
    private static readonly string[] FiscalFlowModules = ["Pipeline", "Validation", "Transmission", "Documents"];

    private static readonly string[] FiscalFlowNamespaces =
        [.. FiscalFlowModules.Select(m => $"Liakont.Modules.{m}")];

    [Fact]
    public void Ged_production_assemblies_carry_no_fiscal_dependency()
    {
        // NetArchTest (IL) : aucun type des assemblies de production GED ne dépend d'un namespace du flux
        // fiscal d'émission (F19 §7). Non vacuous : Ged.Infrastructure porte GedModuleRegistration (types +
        // dépendances réels). Domain/Application/Contracts sont couverts en même temps (le silo grandira).
        var gedProductionAssemblies = new[]
        {
            typeof(GedModuleRegistration).Assembly,                                  // Ged.Infrastructure
            typeof(Liakont.Modules.Ged.Domain.IGedDomainMarker).Assembly,           // Ged.Domain
            typeof(Liakont.Modules.Ged.Application.IGedApplicationMarker).Assembly,  // Ged.Application
            typeof(Liakont.Modules.Ged.Contracts.IGedContractsMarker).Assembly,      // Ged.Contracts
        };

        var offenders = new List<string>();
        foreach (var assembly in gedProductionAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(FiscalFlowNamespaces)
                .GetResult();

            if (!result.IsSuccessful)
            {
                var failing = string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>());
                offenders.Add($"{assembly.GetName().Name} -> [{failing}]");
            }
        }

        var reason =
            "le module GED est un silo isolé du flux fiscal (F19 §7) : aucun type de production GED ne doit "
            + "dépendre de Pipeline/Validation/Transmission/Documents — fautifs : {0}";
        offenders.Should().BeEmpty(reason, string.Join(" ; ", offenders));
    }

    [Fact]
    public void Ged_layers_only_reference_other_modules_through_their_Contracts()
    {
        var gedProjects = EnumerateProductionProjects("Ged");

        var floorReason = $"l'arbre source doit exposer les .csproj de production du module GED (trouvés : {gedProjects.Count})";
        gedProjects.Should().HaveCountGreaterThanOrEqualTo(MinimumGedProductionProjects, floorReason);

        var offenders = new List<string>();
        foreach (var csproj in gedProjects)
        {
            foreach (var reference in ProjectReferenceNames(csproj))
            {
                var isAnotherBusinessModule =
                    reference.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                    && !reference.StartsWith("Liakont.Modules.Ged.", StringComparison.Ordinal);

                if (isAnotherBusinessModule && !reference.EndsWith(".Contracts", StringComparison.Ordinal))
                {
                    offenders.Add($"{csproj.Name} → {reference}");
                }
            }
        }

        var boundaryReason =
            "un module n'accède à un autre que par ses Contracts (module-rules §3, CLAUDE.md n°14) — "
            + "références fautives : {0}";
        offenders.Should().BeEmpty(boundaryReason, string.Join(" ; ", offenders));
    }

    [Fact]
    public void Fiscal_flow_modules_never_reference_Ged()
    {
        var offenders = new List<string>();

        foreach (var module in FiscalFlowModules)
        {
            var projects = EnumerateProductionProjects(module);

            projects.Should().NotBeEmpty(
                "l'arbre source doit exposer le(s) .csproj de production du module fiscal {0}",
                module);

            foreach (var csproj in projects)
            {
                var badRefs = ProjectReferenceNames(csproj)
                    .Where(name => name.StartsWith("Liakont.Modules.Ged", StringComparison.Ordinal));

                foreach (var badRef in badRefs)
                {
                    offenders.Add($"{csproj.Name} → {badRef}");
                }
            }
        }

        var boundaryReason =
            "le flux fiscal (Pipeline/Validation/Transmission/Documents — liste explicite F19 §7) IGNORE la "
            + "GED : aucune dépendance vers Ged.* (P1) — références fautives : {0}";
        offenders.Should().BeEmpty(boundaryReason, string.Join(" ; ", offenders));
    }

    private static IEnumerable<string> ProjectReferenceNames(FileInfo csproj)
    {
        return XDocument.Load(csproj.FullName)
            .Descendants("ProjectReference")
            .Attributes("Include")
            .Select(a => Path.GetFileNameWithoutExtension(a.Value));
    }

    /// <summary>
    /// Énumère, depuis l'arbre SOURCE, les <c>.csproj</c> de PRODUCTION d'un module (exclut les projets de
    /// test <c>*.Tests.*</c>, qui référencent légitimement Domain/Infrastructure et n'entrent pas dans la
    /// frontière inter-modules).
    /// </summary>
    private static List<FileInfo> EnumerateProductionProjects(string module)
    {
        var moduleRoot = Path.Combine(FindRepoRoot(), "src", "Modules", module);
        if (!Directory.Exists(moduleRoot))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(moduleRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Contains(".Tests.", StringComparison.Ordinal))
            .Select(path => new FileInfo(path))
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Liakont.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
