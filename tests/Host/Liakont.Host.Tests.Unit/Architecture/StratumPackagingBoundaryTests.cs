namespace Liakont.Host.Tests.Unit.Architecture;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
/// Ce test échoue dès qu'un tel couplage de direction est introduit.
/// </para>
/// <para>
/// Hors périmètre (cf. description RDF13) : le couplage par chaîne au runtime
/// (<c>ReflectionPermissionCatalog</c> filtre <c>"Liakont."</c>) ne casse pas le packaging.
/// </para>
/// </summary>
public sealed class StratumPackagingBoundaryTests
{
    // Plancher de cohérence : sans assemblies à inspecter, le test passerait à VIDE (faux vert).
    // Le socle vendored embarque largement plus d'assemblies produit que ce seuil (≈24 au moment
    // de RDF13) — descendre sous ce plancher signale un bin incomplet, pas un succès.
    private const int MinimumStratumAssemblies = 10;

    [Fact]
    public void No_Stratum_Assembly_Depends_On_Liakont()
    {
        var stratumAssemblies = LoadStratumProductionAssemblies();

        var floorReason =
            "le bin de test doit contenir les assemblies socle Stratum.* à garder ; un plancher non "
            + "atteint trahit une énumération quasi vide qui passerait à tort (assemblies trouvés : {0})";
        stratumAssemblies.Should().HaveCountGreaterThanOrEqualTo(MinimumStratumAssemblies, floorReason, stratumAssemblies.Length);

        var offenders = stratumAssemblies
            .Select(asm => (
                Name: asm.GetName().Name,
                Result: Types.InAssembly(asm).Should().NotHaveDependencyOn("Liakont").GetResult()))
            .Where(x => !x.Result.IsSuccessful)
            .Select(x => $"{x.Name} -> [{string.Join(", ", x.Result.FailingTypeNames ?? Enumerable.Empty<string>())}]")
            .ToList();

        var boundaryReason =
            "frontière de packaging ADR-0001 : un assembly Stratum.* ne doit JAMAIS dépendre de Liakont.* "
            + "(le socle resterait non reversable en package NuGet) — fautifs : {0}";
        offenders.Should().BeEmpty(boundaryReason, string.Join(" ; ", offenders));
    }

    /// <summary>
    /// Énumère les assemblies socle <c>Stratum.*</c> PRODUIT présents dans le répertoire de sortie :
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
            // défaut (le Host référence tout le socle) — cohérent avec ce que NetArchTest inspecte.
            loaded.Add(Assembly.Load(new AssemblyName(name)));
        }

        return loaded
            .DistinctBy(a => a.GetName().Name)
            .ToArray();
    }
}
