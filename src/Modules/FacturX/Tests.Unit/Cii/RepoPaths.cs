namespace Liakont.Modules.FacturX.Tests.Unit.Cii;

using System.IO;

/// <summary>
/// Localise des ressources du dépôt depuis le répertoire d'exécution des tests (même stratégie que
/// <c>FacturXBoundaryTests</c> : remonter jusqu'à <c>src/Liakont.sln</c>). Sert à charger les golden
/// files du sérialiseur, conservés dans l'arbre source.
/// </summary>
internal static class RepoPaths
{
    /// <summary>Racine du dépôt (répertoire contenant <c>src/Liakont.sln</c>).</summary>
    public static string FindRepoRoot()
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
            $"Racine du dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }

    /// <summary>Répertoire des golden files CII (arbre source du projet de tests).</summary>
    public static string GoldenDir() =>
        Path.Combine(
            FindRepoRoot(), "src", "Modules", "FacturX", "Tests.Unit", "Cii", "GoldenFiles");
}
