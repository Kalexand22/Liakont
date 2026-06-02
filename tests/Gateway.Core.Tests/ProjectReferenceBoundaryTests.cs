using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Conformat.Gateway.Core.Tests
{
    /// <summary>
    /// Garde automatisée des frontières de références inter-projets (blueprint.md §6,
    /// CLAUDE.md règles 6 et 14). Le compilateur ne casse QUE sur un cycle (ex. Core → plug-in) ;
    /// les autres violations (App → Core, plug-in → plug-in, App → SQLite) compileraient sans
    /// erreur. Cet invariant — le plus structurant du produit — est donc vérifié ici en lisant
    /// les &lt;ProjectReference&gt; des .csproj de src/, et non supposé acquis. Exécuté par verify-fast.
    /// </summary>
    public class ProjectReferenceBoundaryTests
    {
        // Nom de projet -> noms des projets qu'il référence (ProjectReference).
        private static readonly IReadOnlyDictionary<string, string[]> ProjectReferences = LoadReferences("ProjectReference", asProjectName: true);

        // Nom de projet -> identifiants des packages NuGet qu'il référence (PackageReference).
        private static readonly IReadOnlyDictionary<string, string[]> PackageReferences = LoadReferences("PackageReference", asProjectName: false);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Gateway.sln")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                throw new InvalidOperationException(
                    "Racine du dépôt introuvable (aucun parent ne contient src/Gateway.sln).");
            }

            return dir.FullName;
        }

        private static IReadOnlyDictionary<string, string[]> LoadReferences(string element, bool asProjectName)
        {
            var srcDir = Path.Combine(FindRepoRoot(), "src");
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var csproj in Directory.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
            {
                var project = Path.GetFileNameWithoutExtension(csproj);
                var references = XDocument.Load(csproj)
                    .Descendants(element)
                    .Select(e => (string)e.Attribute("Include"))
                    .Where(include => !string.IsNullOrWhiteSpace(include))
                    // ProjectReference : Include est un chemin .csproj -> nom de projet ;
                    // PackageReference : Include est déjà l'identifiant du package.
                    .Select(include => asProjectName ? Path.GetFileNameWithoutExtension(include) : include)
                    .ToArray();
                map[project] = references;
            }

            return map;
        }

        private static bool IsPlugin(string project) =>
            project.StartsWith("Gateway.PaClients.", StringComparison.Ordinal) ||
            project.StartsWith("Gateway.Adapters.", StringComparison.Ordinal);

        private static string[] ReferencesOf(string project)
        {
            Assert.True(ProjectReferences.ContainsKey(project), $"Projet attendu absent de src/ : {project}");
            return ProjectReferences[project];
        }

        private static string[] PackagesOf(string project)
        {
            Assert.True(PackageReferences.ContainsKey(project), $"Projet attendu absent de src/ : {project}");
            return PackageReferences[project];
        }

        [Fact]
        public void Core_ne_reference_aucun_projet()
        {
            // Le Core est le produit générique : il ne référence aucun plug-in, ni quoi que ce soit d'autre.
            Assert.Empty(ReferencesOf("Gateway.Core"));
        }

        [Fact]
        public void Api_ne_reference_aucun_projet()
        {
            // Contrats purs : si Api référençait le Core, la console l'atteindrait par transitivité.
            Assert.Empty(ReferencesOf("Gateway.Api"));
        }

        [Fact]
        public void ApiClient_ne_reference_que_Api()
        {
            Assert.All(ReferencesOf("Gateway.ApiClient"), r => Assert.Equal("Gateway.Api", r));
        }

        [Fact]
        public void App_ne_reference_que_Api_et_ApiClient()
        {
            // La console WPF ne voit jamais le Core, les plug-ins ni SQLite (CLAUDE.md règle 6).
            var autorises = new[] { "Gateway.Api", "Gateway.ApiClient" };
            Assert.All(ReferencesOf("Gateway.App"), r => Assert.Contains(r, autorises));
        }

        [Fact]
        public void Chaque_plugin_ne_reference_que_le_Core()
        {
            // Plug-ins (PaClients.*, Adapters.*) : uniquement le Core, jamais un autre plug-in.
            var plugins = ProjectReferences.Keys.Where(IsPlugin).ToArray();
            Assert.NotEmpty(plugins); // garde-fou : la découverte doit trouver les plug-ins
            foreach (var plugin in plugins)
            {
                Assert.All(ReferencesOf(plugin), r => Assert.Equal("Gateway.Core", r));
            }
        }

        [Fact]
        public void App_ne_reference_aucun_package_de_persistance()
        {
            // La console ne touche JAMAIS la base directement (CLAUDE.md règle 6) : elle lit/écrit via
            // l'API du Service (Gateway.ApiClient). Une fuite App → SQLite passerait par un
            // <PackageReference> (System.Data.SQLite.Core / Dapper), invisible au contrôle des
            // <ProjectReference> ci-dessus — d'où ce garde-fou complémentaire sur les packages.
            var packagesInterdits = new[] { "System.Data.SQLite", "Dapper" };
            Assert.DoesNotContain(
                PackagesOf("Gateway.App"),
                package => packagesInterdits.Any(
                    interdit => package.StartsWith(interdit, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
