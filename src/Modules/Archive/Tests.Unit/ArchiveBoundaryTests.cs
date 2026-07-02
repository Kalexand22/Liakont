namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

/// <summary>
/// Garde de frontière du module Archive (F19 §5.1, module-rules §3, CLAUDE.md n°14) : la surface d'archivage
/// GÉNÉRIQUE (GED07) reste une projection PLATE locale (<c>ArchiveIndexAxis</c>) et ne tire JAMAIS le module
/// GED dans Archive. En particulier, <c>Archive.Contracts</c> ne référence pas <c>DocumentAxisLink</c> (type du
/// module GED) — c'est la couche GED qui convertit ses liens vers la projection au point d'appel (API01c).
///
/// Scan DÉCLARATIF des <c>.csproj</c> (modèle <c>GedBoundaryTests</c>/<c>StratumPackagingBoundaryTests</c>) :
/// couvre la « référence sèche » (une <c>ProjectReference</c> ajoutée mais dont le type n'est pas encore
/// utilisé), que l'IL ne verrait pas.
/// </summary>
public sealed class ArchiveBoundaryTests
{
    // Plancher de cohérence : sans projets à inspecter, la garde passerait à VIDE (faux vert). Le module Archive
    // expose au moins Contracts/Domain/Application/Infrastructure en production.
    private const int MinimumArchiveProductionProjects = 4;

    [Fact]
    public void Archive_production_projects_never_reference_the_Ged_module()
    {
        List<FileInfo> archiveProjects = EnumerateProductionProjects("Archive");

        archiveProjects.Should().HaveCountGreaterThanOrEqualTo(
            MinimumArchiveProductionProjects,
            "l'arbre source doit exposer les .csproj de production du module Archive (trouvés : {0})",
            archiveProjects.Count);

        var offenders = new List<string>();
        foreach (FileInfo csproj in archiveProjects)
        {
            foreach (string reference in ProjectReferenceNames(csproj))
            {
                if (reference.StartsWith("Liakont.Modules.Ged", StringComparison.Ordinal))
                {
                    offenders.Add($"{csproj.Name} → {reference}");
                }
            }
        }

        var reason =
            "la surface d'archivage générique (GED07) est une projection PLATE locale (ArchiveIndexAxis) : "
            + "Archive ne référence JAMAIS le module GED (Archive.Contracts ne réf. pas DocumentAxisLink, F19 §5.1) "
            + "— références fautives : {0}";
        offenders.Should().BeEmpty(reason, string.Join(" ; ", offenders));
    }

    private static IEnumerable<string> ProjectReferenceNames(FileInfo csproj)
    {
        return XDocument.Load(csproj.FullName)
            .Descendants("ProjectReference")
            .Attributes("Include")
            .Select(a => Path.GetFileNameWithoutExtension(a.Value));
    }

    private static List<FileInfo> EnumerateProductionProjects(string module)
    {
        string moduleRoot = Path.Combine(FindRepoRoot(), "src", "Modules", module);
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
