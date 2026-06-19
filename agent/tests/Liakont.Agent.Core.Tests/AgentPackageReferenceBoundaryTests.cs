namespace Liakont.Agent.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Frontière DÉCLARATIVE de l'agent (CLAUDE.md n°6, chaque violation = P1 ; F17 §6 : le client Wacom
/// vit HORS agent/). Complète <see cref="AgentBoundaryTests"/> qui, lui, inspecte les références au
/// niveau IL (GetReferencedAssemblies) : un <c>&lt;PackageReference&gt;</c> purement DÉCLARATIF —
/// jamais exercé par du code (ex. un SDK natif tiré pour ses cibles MSBuild / son contenu, sans type
/// C# référencé) — n'apparaît dans AUCUN assembly compilé et échappe donc à l'inspection IL.
/// <para>
/// Ce test lit les .csproj, Directory.Build.props, Directory.Packages.props ET Directory.Build.targets
/// SOUS agent/ et exige que chaque <c>&lt;PackageReference&gt;</c> ou
/// <c>&lt;GlobalPackageReference&gt;</c> figure dans une liste BLANCHE FERMÉE codée ICI. Elle
/// n'est volontairement PAS dérivée de Directory.Packages.props : sinon il suffirait d'ajouter le
/// paquet interdit AUX DEUX fichiers (catalogue + csproj) pour passer — la garde doit être un choix
/// humain explicite, exactement comme <see cref="AgentBoundaryTests"/> énumère ses tierces autorisées.
/// </para>
/// Tiers : les paquets de build/analyse (Directory.Build.props) valent pour TOUS les projets ; les
/// projets « production » (agent/src) n'ajoutent que le runtime déclaré (Newtonsoft.Json, SQLite) ;
/// les projets de test (agent/tests) ajoutent en plus le harnais xUnit.
/// </summary>
public class AgentPackageReferenceBoundaryTests
{
    // Paquets de build / analyse, appliqués à TOUS les projets agent via agent/Directory.Build.props
    // (PrivateAssets="all" : ne ruissellent pas, ne sont pas des libs d'exécution).
    private static readonly string[] BuildTimePackages =
    {
        "Microsoft.NETFramework.ReferenceAssemblies",
        "StyleCop.Analyzers",
    };

    // Dépendances RUNTIME autorisées des projets de production (agent/src). MÊME ensemble que
    // AgentBoundaryTests.AllowedThirdPartyAssemblies (catalogue central, blueprint.md §5 / F12 §3.4).
    private static readonly string[] ProductionRuntimePackages =
    {
        "Newtonsoft.Json",
        "System.Data.SQLite.Core",
    };

    // Harnais de test, autorisé UNIQUEMENT pour les projets sous agent/tests.
    private static readonly string[] TestOnlyPackages =
    {
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "xunit.runner.visualstudio",
        "FluentAssertions",
    };

    // Capture le nom du paquet sur la balise ouvrante d'un PackageReference ou GlobalPackageReference
    // (auto-fermée ou avec enfants). Couvre aussi Directory.Packages.props et Directory.Build.targets
    // où un GlobalPackageReference s'applique à tous les projets sans <PackageReference> dans aucun
    // csproj. [^>]* reste dans la balise (les classes négatives traversent les sauts de ligne).
    // Accepte les guillemets SIMPLES et DOUBLES (syntaxe MSBuild valide) via rétroréférence \1 :
    // groupe 1 = caractère de guillemet ouvrant, groupe 2 = nom du paquet.
    // Note : <PackageVersion> est volontairement exclu — c'est une déclaration de version, pas une
    // référence qui tire le paquet.
    private static readonly Regex PackageReferenceInclude = new Regex(
        "<(?:PackageReference|GlobalPackageReference)\\b[^>]*?\\bInclude\\s*=\\s*([\"'])([^\"']+)\\1",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Agent_PackageReferences_are_all_on_the_closed_allowlist()
    {
        string agentRoot = FindAgentRoot();

        string[] msbuildFiles = Directory
            .EnumerateFiles(agentRoot, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(agentRoot, "Directory.Build.props", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(agentRoot, "Directory.Packages.props", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(agentRoot, "Directory.Build.targets", SearchOption.AllDirectories))
            .Where(p => !IsUnderBinOrObj(p))
            .ToArray();

        msbuildFiles.Should().NotBeEmpty(
            "les .csproj de l'agent doivent être trouvés depuis la sortie de test (racine agent/ via Liakont.Agent.sln)");

        var violations = new List<string>();
        foreach (string file in msbuildFiles)
        {
            var allowed = AllowedFor(file, agentRoot);
            foreach (string package in ViolatingPackages(File.ReadAllText(file), allowed))
            {
                violations.Add($"{RelativeToAgentRoot(file, agentRoot)} -> PackageReference {package}");
            }
        }

        violations.Should().BeEmpty(
            "tout <PackageReference> sous agent/ doit figurer dans la liste blanche fermée : le SDK Wacom natif " +
            "et toute lib non déclarée sont interdits dans l'agent (CLAUDE.md n°6, F17 §6). Ajouter une " +
            "dépendance légitime = l'ajouter ICI, en connaissance de cause.");
    }

    // Self-test de la garde elle-même (CLAUDE.md : tester dans LES DEUX SENS — la garde doit échouer
    // quand c'est cassé ET ne pas échouer quand c'est sain). Prouve que le SDK Wacom est bien détecté
    // et qu'un paquet autorisé ne lève pas de faux positif, sans polluer un vrai .csproj.
    [Fact]
    public void Declarative_inspection_flags_an_undeclared_package_and_passes_an_allowed_one()
    {
        var allowed = new HashSet<string>(ProductionRuntimePackages, StringComparer.OrdinalIgnoreCase);

        const string withWacom =
            "<Project><ItemGroup><PackageReference Include=\"Wacom.Stu.Sdk\" Version=\"1.0.0\" /></ItemGroup></Project>";

        // L'echappatoire de la gestion centralisee : un GlobalPackageReference (catalogue) vise tous
        // les projets sans aucun <PackageReference> dans un csproj — doit etre attrape lui aussi.
        const string withGlobalWacom =
            "<Project><ItemGroup><GlobalPackageReference Include=\"Wacom.Stu.Sdk\" Version=\"1.0.0\" /></ItemGroup></Project>";

        // <PackageVersion> seul (sans reference) ne tire rien : ne doit PAS etre signale.
        const string versionOnly =
            "<Project><ItemGroup><PackageVersion Include=\"Wacom.Stu.Sdk\" Version=\"1.0.0\" /></ItemGroup></Project>";
        const string clean =
            "<Project><ItemGroup><PackageReference Include=\"Newtonsoft.Json\" /></ItemGroup></Project>";

        // Guillemets simples : syntaxe MSBuild valide qui devait s'echapper auparavant.
        const string withSingleQuoteWacom =
            "<Project><ItemGroup><PackageReference Include='Wacom.Stu.Sdk' Version='1.0.0' /></ItemGroup></Project>";

        ViolatingPackages(withWacom, allowed).Should().ContainSingle().Which.Should().Be("Wacom.Stu.Sdk");
        ViolatingPackages(withGlobalWacom, allowed).Should().ContainSingle().Which.Should().Be("Wacom.Stu.Sdk");
        ViolatingPackages(withSingleQuoteWacom, allowed).Should().ContainSingle().Which.Should().Be("Wacom.Stu.Sdk");
        ViolatingPackages(versionOnly, allowed).Should().BeEmpty();
        ViolatingPackages(clean, allowed).Should().BeEmpty();
    }

    private static string[] ViolatingPackages(string projectXml, HashSet<string> allowed)
    {
        return PackageReferenceInclude.Matches(projectXml)
            .Cast<Match>()
            .Select(m => m.Groups[2].Value)
            .Where(name => !allowed.Contains(name))
            .ToArray();
    }

    private static HashSet<string> AllowedFor(string file, string agentRoot)
    {
        var set = new HashSet<string>(BuildTimePackages, StringComparer.OrdinalIgnoreCase);
        string rel = RelativeToAgentRoot(file, agentRoot);
        if (rel.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
        {
            // Un projet de test peut référencer le runtime de production (ex. SQLite pour exercer la file) + le harnais.
            foreach (string p in ProductionRuntimePackages)
            {
                set.Add(p);
            }

            foreach (string p in TestOnlyPackages)
            {
                set.Add(p);
            }
        }
        else if (rel.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string p in ProductionRuntimePackages)
            {
                set.Add(p);
            }
        }

        // Sinon (Directory.Build.props à la racine agent/) : seuls les paquets de build/analyse.
        return set;
    }

    private static string RelativeToAgentRoot(string file, string agentRoot)
    {
        string f = file.Replace('\\', '/');
        string root = agentRoot.Replace('\\', '/').TrimEnd('/') + "/";
        return f.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? f.Substring(root.Length) : f;
    }

    private static bool IsUnderBinOrObj(string path)
    {
        string n = path.Replace('\\', '/');
        return n.Contains("/bin/") || n.Contains("/obj/");
    }

    // Remonte depuis la sortie de test jusqu'au répertoire racine de l'agent (celui qui porte
    // Liakont.Agent.sln). Ne dépend pas d'un chemin relatif fixe (robuste au RID/Configuration).
    private static string FindAgentRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Liakont.Agent.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Répertoire racine de l'agent (contenant Liakont.Agent.sln) introuvable depuis " + AppContext.BaseDirectory);
    }
}
