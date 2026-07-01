namespace Liakont.Modules.Ged.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

/// <summary>
/// Gardes de frontière du module GED (F19 §7/§8, module-rules §3, CLAUDE.md n°14), sur le modèle DÉCLARATIF
/// et AUTORITAIRE de <c>StratumPackagingBoundaryTests</c> : scan des <c>.csproj</c> depuis l'arbre SOURCE.
/// <para>
/// L'approche déclarative couvre le cas de la « référence sèche » (une <c>ProjectReference</c> ajoutée mais
/// dont le type n'est pas encore utilisé) — précisément celui qui casse une frontière sans qu'un test IL au
/// niveau des types ne le voie — et reste significative même quand le module GED, au stade scaffold, ne
/// référence encore aucun autre module.
/// </para>
/// </summary>
public sealed class GedBoundaryTests
{
    // Planchers de cohérence : sans projets à inspecter, une garde passerait à VIDE (faux vert). Le module
    // GED expose 5 couches de production (Contracts/Domain/Application/Infrastructure/Web) ; les 4 modules du
    // flux fiscal exposent au moins un .csproj de production chacun.
    private const int MinimumGedProductionProjects = 5;

    private static readonly string[] FiscalFlowModules = ["Pipeline", "Validation", "Transmission", "Documents"];

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
            "le flux fiscal (Pipeline/Validation/Transmission/Documents) IGNORE la GED : aucune dépendance "
            + "vers Ged.* (P1, F19 §7) — références fautives : {0}";
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
