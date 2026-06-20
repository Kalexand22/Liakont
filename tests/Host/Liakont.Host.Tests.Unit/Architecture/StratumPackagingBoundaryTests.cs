namespace Liakont.Host.Tests.Unit.Architecture;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Garde de frontière de PACKAGING (RDF13 — RL-SOCLE-4, ADR-0001) : aucun assembly socle
/// <c>Stratum.*</c> ne dépend d'un assembly produit <c>Liakont.*</c>.
/// <para>
/// La garde de provenance (<c>tools/verify-fast.ps1</c> + <c>socle-baseline.sha1</c>) ne juge que
/// le CONTENU des fichiers socle (hash), pas la DIRECTION des dépendances. Comme la solution
/// contient les deux côtés (socle vendored + produit), un <c>.csproj</c> <c>Stratum.*</c> pourrait
/// référencer du code <c>Liakont.*</c> et compiler ; <c>verify-fast</c> resterait vert. Le fichier
/// socle deviendrait alors NON REVERSABLE en package NuGet (casse l'option D « extraire le socle »).
/// </para>
/// <para>
/// Deux gardes complémentaires, par niveaux distincts :
/// <list type="bullet">
/// <item><description>
/// <see cref="No_Stratum_Csproj_Declares_A_Dependency_On_Liakont"/> — DÉCLARATIVE et AUTORITAIRE :
/// scanne TOUS les <c>.csproj</c> socle produit depuis l'arbre SOURCE et échoue sur le moindre
/// <c>ProjectReference</c>/<c>PackageReference</c> vers <c>Liakont.*</c>. Couvre le cas « référence
/// sèche » (référence ajoutée mais type non encore utilisé) — précisément celui qui casse le
/// packaging — et l'ensemble des projets socle, sans dépendre de ce qui est copié dans un bin.
/// </description></item>
/// <item><description>
/// <see cref="No_Stratum_Assembly_Has_An_Il_Dependency_On_Liakont"/> — IL via NetArchTest (la garde
/// prescrite par RDF13) : inspecte au niveau IL les assemblies socle chargés et échoue sur toute
/// dépendance de TYPE vers <c>Liakont</c>. Complète la garde déclarative côté usage effectif.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Hors périmètre (cf. description RDF13) : le couplage par chaîne au runtime
/// (<c>ReflectionPermissionCatalog</c> filtre <c>"Liakont."</c>) ne casse pas le packaging.
/// </para>
/// </summary>
public sealed class StratumPackagingBoundaryTests
{
    // Plancher de cohérence : sans projets/assemblies à inspecter, une garde passerait à VIDE (faux
    // vert). Le socle vendored embarque 25 projets produit Stratum.* au moment de RDF13 ; ce plancher
    // (volontairement sous le réel pour tolérer l'archivage d'un module) échoue si l'énumération
    // s'effondre — signe d'un arbre source ou d'un bin incomplet, pas d'un succès.
    private const int MinimumStratumProjects = 20;

    [Fact]
    public void No_Stratum_Csproj_Declares_A_Dependency_On_Liakont()
    {
        var projects = EnumerateProductionStratumProjects();

        var floorReason = $"l'arbre source doit exposer les .csproj socle Stratum.* à garder (trouvés : {projects.Count})";
        projects.Should().HaveCountGreaterThanOrEqualTo(MinimumStratumProjects, floorReason);

        var offenders = new List<string>();
        foreach (var csproj in projects)
        {
            var doc = XDocument.Load(csproj.FullName);

            var projectRefs = doc.Descendants("ProjectReference")
                .Attributes("Include")
                .Select(a => Path.GetFileNameWithoutExtension(a.Value))
                .Where(name => name.StartsWith("Liakont.", StringComparison.Ordinal))
                .Select(name => $"ProjectReference→{name}");

            var packageRefs = doc.Descendants("PackageReference")
                .Attributes("Include")
                .Select(a => a.Value)
                .Where(name => name.StartsWith("Liakont.", StringComparison.Ordinal))
                .Select(name => $"PackageReference→{name}");

            var bad = projectRefs.Concat(packageRefs).ToArray();
            if (bad.Length > 0)
            {
                offenders.Add($"{csproj.Name} : {string.Join(", ", bad)}");
            }
        }

        var boundaryReason =
            "frontière de packaging ADR-0001 : un .csproj Stratum.* ne doit JAMAIS déclarer de référence "
            + "(projet ou package) vers Liakont.* (sinon socle non reversable en NuGet) — fautifs : {0}";
        offenders.Should().BeEmpty(boundaryReason, string.Join(" ; ", offenders));
    }

    [Fact]
    public void No_Stratum_Assembly_Has_An_Il_Dependency_On_Liakont()
    {
        var stratumAssemblies = LoadStratumProductionAssemblies();

        var emptyReason =
            "le bin de test doit contenir des assemblies socle Stratum.* à inspecter au niveau IL ; une "
            + "énumération vide passerait à tort (la couverture complète des projets est tenue par la "
            + $"garde déclarative {nameof(No_Stratum_Csproj_Declares_A_Dependency_On_Liakont)})";
        stratumAssemblies.Should().NotBeEmpty(emptyReason);

        var offenders = stratumAssemblies
            .Select(asm => (
                Name: asm.GetName().Name,
                Result: Types.InAssembly(asm).Should().NotHaveDependencyOn("Liakont").GetResult()))
            .Where(x => !x.Result.IsSuccessful)
            .Select(x => $"{x.Name} -> [{string.Join(", ", x.Result.FailingTypeNames ?? Enumerable.Empty<string>())}]")
            .ToList();

        var boundaryReason =
            "frontière de packaging ADR-0001 (IL) : un assembly Stratum.* ne doit JAMAIS dépendre d'un "
            + "type Liakont.* — fautifs : {0}";
        offenders.Should().BeEmpty(boundaryReason, string.Join(" ; ", offenders));
    }

    /// <summary>
    /// Énumère, depuis l'arbre SOURCE, les <c>.csproj</c> socle <c>Stratum.*</c> PRODUIT : exclut les
    /// projets de test (<c>*.Tests.*</c>), qui ne font pas partie du socle packagé.
    /// </summary>
    private static List<FileInfo> EnumerateProductionStratumProjects()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        return Directory
            .EnumerateFiles(srcRoot, "Stratum.*.csproj", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Contains(".Tests.", StringComparison.Ordinal))
            .Select(path => new FileInfo(path))
            .ToList();
    }

    /// <summary>
    /// Charge les assemblies socle <c>Stratum.*</c> PRODUIT présents dans le répertoire de sortie :
    /// exclut les satellites de ressources (<c>*.resources.dll</c>) et les assemblies de test
    /// (<c>*.Tests.*</c>), qui ne font pas partie du socle packagé.
    /// </summary>
    private static Assembly[] LoadStratumProductionAssemblies()
    {
        var loaded = new List<Assembly>();
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Stratum.*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(".resources", StringComparison.Ordinal)
                || name.Contains(".Tests.", StringComparison.Ordinal))
            {
                continue;
            }

            // Assembly.Load par nom simple renvoie l'instance déjà chargée dans le contexte par
            // défaut (le Host référence le socle) — cohérent avec ce que NetArchTest inspecte.
            loaded.Add(Assembly.Load(new AssemblyName(name)));
        }

        return loaded
            .DistinctBy(a => a.GetName().Name)
            .ToArray();
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
