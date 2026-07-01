namespace Liakont.Agent.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Liakont.Agent.Contracts.ContractTests;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Xunit;

/// <summary>
/// Gardes DÉCLARATIVES du couplage cross-solution agent ↔ plateforme (RDL11, redline ADR-0005).
/// Trois angles morts laissés par les gardes existantes :
/// <list type="number">
/// <item><b>A5-purity-1</b> — la pureté de <c>Liakont.Agent.Contracts</c> (netstandard2.0, zéro
/// <c>PackageReference</c> effectif) n'a AUCUNE garde déclarative : <see cref="ContractsPurityTests"/>
/// inspecte l'IL (un PackageReference purement déclaratif y échappe) et
/// <see cref="AgentPackageReferenceBoundaryTests"/> ne scanne que <c>agent/</c>, pas
/// <c>src/Contracts/</c>. Un analyseur en <c>PrivateAssets=all</c> ou un passage à net8.0 casserait
/// la consommabilité net48 (publication NuGet) sans test rouge. La garde couvre également le vecteur
/// CPM (<c>GlobalPackageReference</c> dans <c>Directory.Packages.props</c>) : la chaîne MSBuild
/// inclut désormais les <c>Directory.Packages.props</c> à chaque niveau.
/// <b>Limitation assumée :</b> <see cref="MsbuildChain"/> lit <c>Directory.Build.props</c> et
/// <c>Directory.Packages.props</c> littéralement et ne suit PAS les chaînes <c>&lt;Import&gt;</c> ;
/// un analyseur injecté via un fichier importé échapperait à la garde. Aujourd'hui les props racine
/// déclarent les paquets directement, la garde est donc exhaustive pour la configuration réelle.</item>
/// <item><b>A5-coupling-2</b> — le couplage cross-solution (6 <c>ProjectReference</c> vers
/// <c>src/Contracts</c>, 6 <c>Compile</c>-link de <c>tests/_shared/contract-v1</c>, 1 copie de fixtures)
/// n'est verrouillé par aucun test : un nouveau chemin <c>..\..\..\src</c> ou <c>..\..\..\tests</c> dans un
/// csproj agent passerait inaperçu.</item>
/// <item><b>A5-coupling-3</b> — les empreintes figées du contrat (golden cross-runtime) sont exercées
/// côté agent UNIQUEMENT par des fichiers <c>Compile</c>-link ; retirer le lien ferait disparaître la
/// preuve net48 SILENCIEUSEMENT (faux-vert à la scission du dépôt agent, ADR-0005).</item>
/// </list>
/// </summary>
public sealed class ContractCouplingGuardTests
{
    // Nombre de documents golden de référence (PIV03) exercés côté agent. Toute évolution VOLONTAIRE du
    // jeu de fixtures (ajout/retrait d'un document de contrat) doit mettre ce compte à jour — et passer
    // la gate humaine. Verrouille A5-coupling-3 : un Compile-link retiré ferait chuter ce compte.
    private const int ExpectedFrozenDocumentCount = 8;

    // Couplage cross-solution FIGÉ (csproj agent → chemin résolu relatif à la racine du dépôt). Toute
    // entrée supplémentaire/manquante = échec : ajouter un lien cross-solution est un choix HUMAIN
    // explicite (même esprit que la liste blanche fermée d'AgentPackageReferenceBoundaryTests). Le
    // ..\..\..\src vers le contrat est légitime (ProjectReference) ; les ..\..\..\tests lient les golden
    // partagés + fixtures (preuve cross-runtime F12 §3.4). Avant la bascule NuGet (ADR-0005, RDL10) ces
    // liens deviendront un paquet : la liste documente exactement ce qui est à reporter.
    private static readonly string[] FrozenCrossSolutionLinks =
    {
        "src/Liakont.Agent.Cli/Liakont.Agent.Cli.csproj -> src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj",
        "src/Liakont.Agent.Core/Liakont.Agent.Core.csproj -> src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj",

        // Canal GED (F19 §4.2/§4.6, item GED05a) : Core consomme Contracts.Ged (IManagedExtractor →
        // IngestedDocumentDto), Core.Tests l'exerce (golden RL-39 + frontière AgentBoundaryTests).
        "src/Liakont.Agent.Core/Liakont.Agent.Core.csproj -> src/Contracts/Liakont.Agent.Contracts.Ged/Liakont.Agent.Contracts.Ged.csproj",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> src/Contracts/Liakont.Agent.Contracts.Ged/Liakont.Agent.Contracts.Ged.csproj",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/_shared/contract-v1/IngestedDocumentContractGoldenTests.cs",
        "tests/Liakont.Agent.Adapters.DemoErpA.Tests/Liakont.Agent.Adapters.DemoErpA.Tests.csproj -> src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj",
        "tests/Liakont.Agent.Adapters.DemoErpB.Tests/Liakont.Agent.Adapters.DemoErpB.Tests.csproj -> src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj",
        "tests/Liakont.Agent.Adapters.EncheresV6.Tests/Liakont.Agent.Adapters.EncheresV6.Tests.csproj -> src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/_shared/contract-v1/CanonicalDeterminismTests.cs",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/_shared/contract-v1/CanonicalEnumGuardTests.cs",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/_shared/contract-v1/ContractFixtureTests.cs",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/_shared/contract-v1/ContractFixtures.cs",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/_shared/contract-v1/PivotCanonicalReader.cs",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/_shared/contract-v1/PivotContractGoldenTests.cs",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/fixtures/contrat-v1/*.json",
        "tests/Liakont.Agent.Core.Tests/Liakont.Agent.Core.Tests.csproj -> tests/fixtures/contrat-v2/*.json",
    };

    // Nom du paquet (Include) sur un PackageReference / GlobalPackageReference. Guillemets simples ou
    // doubles (rétroréférence \1). Aligné sur AgentPackageReferenceBoundaryTests.
    private static readonly Regex PackageReferenceInclude = new Regex(
        "<(?:PackageReference|GlobalPackageReference)\\b[^>]*?\\bInclude\\s*=\\s*([\"'])([^\"']+)\\1",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Nom du paquet sur un PackageReference Remove (retrait d'un paquet hérité, ex. l'analyseur StyleCop
    // injecté par Directory.Build.props racine que le contrat retire pour rester « zéro PackageReference »).
    private static readonly Regex PackageReferenceRemove = new Regex(
        "<PackageReference\\b[^>]*?\\bRemove\\s*=\\s*([\"'])([^\"']+)\\1",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Valeur d'attribut Include sur N'IMPORTE quel élément d'item (ProjectReference, Compile, None,
    // Content, …). Groupe 1 = élément, 2 = guillemet, 3 = valeur. Sert à détecter les chemins qui
    // ÉCHAPPENT de agent/ vers src/ ou tests/ (couplage cross-solution).
    private static readonly Regex AnyItemInclude = new Regex(
        "<(\\w+)\\b[^>]*?\\bInclude\\s*=\\s*([\"'])([^\"']+)\\2",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TargetFrameworkElement = new Regex(
        "<TargetFramework>\\s*([^<\\s]+)\\s*</TargetFramework>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Contract_projects_are_netstandard20_with_zero_effective_package_reference()
    {
        string repoRoot = FindRepoRoot();

        // Les DEUX projets du contrat agent↔plateforme partagés en netstandard2.0 : le contrat de base
        // (pivot fiscal) ET le contrat GED (Contracts.Ged, F19 §4.2/§4.6, item GED05a). Les deux doivent
        // rester netstandard2.0 + zéro paquet effectif — même exigence de consommabilité net48/.NET 10.
        string[] contractCsprojs =
        {
            Path.Combine(repoRoot, "src", "Contracts", "Liakont.Agent.Contracts", "Liakont.Agent.Contracts.csproj"),
            Path.Combine(repoRoot, "src", "Contracts", "Liakont.Agent.Contracts.Ged", "Liakont.Agent.Contracts.Ged.csproj"),
        };

        foreach (string contractCsproj in contractCsprojs)
        {
            File.Exists(contractCsproj).Should().BeTrue(
                "le projet de contrat agent↔plateforme doit exister à src/Contracts/ (introuvable : " + contractCsproj + ")");

            string csprojText = File.ReadAllText(contractCsproj);

            // 1. Cible netstandard2.0 : seul TFM consommable à la fois par net48 (agent) et net10 (plateforme).
            //    Un passage à net8.0/net10 casserait la consommabilité net48 (condition de la publication NuGet).
            Match tfm = TargetFrameworkElement.Match(csprojText);
            tfm.Success.Should().BeTrue("le contrat doit déclarer un <TargetFramework> unique (pas de TargetFrameworks pluriel)");
            tfm.Groups[1].Value.Should().Be(
                "netstandard2.0",
                "le contrat reste netstandard2.0 — consommable par l'agent net48 ET la plateforme net10 (blueprint.md §3.2)");

            // 2. Zéro PackageReference EFFECTIF, en tenant compte du Remove hérité : le Directory.Build.props
            //    racine injecte StyleCop.Analyzers pour tous les projets ; le contrat doit le RETIRER pour
            //    rester « BCL seul ». Si ce Remove disparaît (ou un paquet est ajouté), l'effectif devient
            //    non vide → échec. C'est la garde DÉCLARATIVE qui manque à ContractsPurityTests (niveau IL).
            IEnumerable<string> msbuildChain = MsbuildChain(Path.GetDirectoryName(contractCsproj)!, repoRoot);
            string[] effective = ComputeEffectivePackages(msbuildChain.Select(File.ReadAllText)).ToArray();

            effective.Should().BeEmpty(
                Path.GetFileNameWithoutExtension(contractCsproj) + " doit rester sans aucune dépendance NuGet effective "
                + "(BCL seul, acceptance SOL02) — un analyseur non retiré ou une lib ajoutée casse la pureté du paquet "
                + "publié. Effectif : " + string.Join(", ", effective));
        }
    }

    [Fact]
    public void Agent_csprojs_cross_solution_links_match_the_frozen_inventory()
    {
        string repoRoot = FindRepoRoot();
        string agentRoot = Path.Combine(repoRoot, "agent");

        string[] csprojs = Directory
            .EnumerateFiles(agentRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsUnderBinOrObj(p))
            .ToArray();

        csprojs.Should().NotBeEmpty("les csproj agent doivent être trouvés (racine agent/ via Liakont.Agent.sln)");

        var discovered = new List<string>();
        foreach (string csproj in csprojs)
        {
            string csprojDirRepoRel = RepoRelative(Path.GetDirectoryName(csproj)!, repoRoot);
            string csprojAgentRel = AgentRelative(csproj, agentRoot);

            foreach (Match m in AnyItemInclude.Matches(File.ReadAllText(csproj)))
            {
                string include = m.Groups[3].Value;
                string resolved = CombineAndNormalize(csprojDirRepoRel, include);
                if (IsCrossSolution(resolved))
                {
                    discovered.Add(csprojAgentRel + " -> " + resolved);
                }
            }
        }

        discovered.Sort(StringComparer.Ordinal);
        var expected = FrozenCrossSolutionLinks.OrderBy(s => s, StringComparer.Ordinal).ToArray();

        string because =
            "tout chemin cross-solution (..\\..\\..\\src ou ..\\..\\..\\tests) d'un csproj agent doit figurer dans "
            + "l'inventaire FIGÉ : un nouveau lien (ou un retrait) est un choix humain à reporter ici ET dans le "
            + "plan de bascule NuGet (ADR-0005, RDL10). Découvert vs figé ci-dessus.";
        discovered.Should().Equal(expected, because);
    }

    [Fact]
    public void Frozen_contract_hashes_are_exercised_agent_side_net48()
    {
        // A5-coupling-3 : la suite golden cross-runtime (empreintes figées) est tirée dans CE projet par
        // des Compile-link (tests/_shared/contract-v1). Si le lien était retiré, le typeof ci-dessous ne
        // COMPILERAIT pas (échec bruyant immédiat) et le compte de cas chuterait — jamais un faux-vert.

        // 0. Ancrage runtime : ce test DOIT tourner sous .NET Framework 4.8 (agent net48).
        //    Si le TFM du projet de test changeait (ex. net8.0), cette assertion deviendrait rouge
        //    immédiatement — fermant le vecteur de faux-vert documenté par RDL11.
        string runtimeBecause =
            "ce test est la preuve cross-runtime net48 ; s'il tournait sous .NET 8/10, il prouverait "
            + "les empreintes sur le MAUVAIS runtime (ADR-0005, RDL11)";
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Should().Contain(
            ".NET Framework", runtimeBecause);

        // 1. Le type golden lié est compilé DANS l'assembly de test AGENT (net48), pas une coquille
        //    importée d'ailleurs : la preuve d'empreinte cross-runtime tourne réellement côté agent.
        string linkBecause =
            "ContractFixtureTests (golden figés) doit être compilé dans l'assembly de test agent via le Compile-link "
            + "tests/_shared/contract-v1 — sinon la preuve d'empreinte net48 disparaît silencieusement (ADR-0005)";
        typeof(ContractFixtureTests).Assembly.Should().BeSameAs(this.GetType().Assembly, linkBecause);

        // 2. Compte des cas exercés == N documents de référence (PIV03). Un Compile-link retiré ou un
        //    document droppé ferait chuter ce compte.
        string[] caseNames = ContractFixtures.DocumentCases
            .Select(c => (string)c[0])
            .ToArray();

        string countBecause =
            "les " + ExpectedFrozenDocumentCount + " documents golden du contrat doivent être exercés côté agent net48 "
            + "(compte des cas) — un retrait du lien ou d'une fixture le révèle ici";
        caseNames.Should().HaveCount(ExpectedFrozenDocumentCount, countBecause);

        // 3. Chaque document se hashe effectivement sur CE runtime (net48) : l'empreinte est exercée, pas
        //    seulement déclarée. (Les VALEURS figées sont vérifiées par ContractFixtureTests, prouvé compilé
        //    ici à l'étape 1 — donc exécuté dans le run agent.)
        foreach (string name in caseNames)
        {
            PivotDocumentDto document = ContractFixtures.GetDocument(name);
            PayloadHasher.ComputeHash(document).Should().MatchRegex(
                "^[0-9a-f]{64}$",
                "l'empreinte SHA-256 du document " + name + " doit se calculer côté agent net48 (hex minuscule, 64 car.)");
        }
    }

    // ── Self-tests des gardes (CLAUDE.md : prouver que la garde échoue quand c'est cassé ET passe quand
    //    c'est sain — sans toucher un vrai csproj). ──
    [Fact]
    public void Effective_package_computation_honours_inherited_remove()
    {
        string propsWithStyleCop =
            "<Project><ItemGroup><PackageReference Include=\"StyleCop.Analyzers\" PrivateAssets=\"all\" /></ItemGroup></Project>";

        // Sain : le csproj retire l'analyseur hérité → effectif vide.
        string csprojRemovingStyleCop =
            "<Project><ItemGroup><PackageReference Remove=\"StyleCop.Analyzers\" /></ItemGroup></Project>";
        var healthy = new List<string> { propsWithStyleCop, csprojRemovingStyleCop };
        ComputeEffectivePackages(healthy).Should().BeEmpty(
            "un Remove hérité annule l'Include du props racine");

        // Cassé n°1 : le Remove disparaît → l'analyseur hérité reste effectif.
        var withoutRemove = new List<string> { propsWithStyleCop, "<Project />" };
        ComputeEffectivePackages(withoutRemove).Should().ContainSingle()
            .Which.Should().Be("StyleCop.Analyzers");

        // Cassé n°2 : une lib runtime est ajoutée sans retrait → effectif non vide.
        string csprojAddingPackage =
            "<Project><ItemGroup><PackageReference Remove=\"StyleCop.Analyzers\" /><PackageReference Include=\"Newtonsoft.Json\" /></ItemGroup></Project>";
        var withAddedPackage = new List<string> { propsWithStyleCop, csprojAddingPackage };
        ComputeEffectivePackages(withAddedPackage).Should().ContainSingle()
            .Which.Should().Be("Newtonsoft.Json");
    }

    [Fact]
    public void Effective_package_computation_detects_cpm_global_package_reference_not_package_version()
    {
        // Vecteur CPM : un GlobalPackageReference dans Directory.Packages.props injecte un analyseur
        // dans TOUS les projets — exactement comme un PackageReference dans Directory.Build.props.
        // La garde doit le détecter. En revanche, <PackageVersion> est une déclaration de version,
        // pas une référence ; elle ne doit PAS entrer dans l'effectif.
        // Directory.Packages.props typique : version déclarée (PackageVersion) + injection globale
        // (GlobalPackageReference). Le GlobalPackageReference DOIT compter ; le PackageVersion non.
        string packagesPropsWithGlobalRef =
            "<Project><ItemGroup>"
            + "<PackageVersion Include=\"Dapper\" Version=\"2.1.35\" />"
            + "<GlobalPackageReference Include=\"SomeAnalyzer\" PrivateAssets=\"all\" />"
            + "</ItemGroup></Project>";

        // Csproj du contrat : aucune référence propre.
        string contractCsproj = "<Project><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup></Project>";

        var withGlobalRef = new List<string> { contractCsproj, packagesPropsWithGlobalRef };
        string globalRefBecause =
            "un GlobalPackageReference dans Directory.Packages.props est une référence effective "
            + "qui doit être détectée par la garde (vecteur CPM — faux-vert sans cette couverture)";
        ComputeEffectivePackages(withGlobalRef).Should().ContainSingle(globalRefBecause)
            .Which.Should().Be("SomeAnalyzer");

        // Un PackageVersion seul (sans GlobalPackageReference) ne doit PAS être compté.
        string packagesPropsVersionOnly =
            "<Project><ItemGroup>"
            + "<PackageVersion Include=\"Dapper\" Version=\"2.1.35\" />"
            + "<PackageVersion Include=\"SomeAnalyzer\" Version=\"1.0.0\" />"
            + "</ItemGroup></Project>";

        var withVersionOnly = new List<string> { contractCsproj, packagesPropsVersionOnly };
        string versionOnlyBecause =
            "un PackageVersion est une déclaration de version CPM, pas une référence ; "
            + "il ne doit pas entrer dans l'effectif de pureté du contrat";
        ComputeEffectivePackages(withVersionOnly).Should().BeEmpty(versionOnlyBecause);
    }

    [Fact]
    public void Cross_solution_detection_flags_an_escape_and_ignores_an_internal_reference()
    {
        // Un lien depuis agent/tests vers tests/_shared échappe (cross-solution).
        string escape = CombineAndNormalize(
            "agent/tests/Liakont.Agent.Core.Tests", "..\\..\\..\\tests\\_shared\\contract-v1\\ContractFixtures.cs");
        escape.Should().Be("tests/_shared/contract-v1/ContractFixtures.cs");
        IsCrossSolution(escape).Should().BeTrue();

        // Un lien vers src/Contracts échappe aussi.
        string toContract = CombineAndNormalize(
            "agent/src/Liakont.Agent.Core", "..\\..\\..\\src\\Contracts\\Liakont.Agent.Contracts\\Liakont.Agent.Contracts.csproj");
        toContract.Should().Be("src/Contracts/Liakont.Agent.Contracts/Liakont.Agent.Contracts.csproj");
        IsCrossSolution(toContract).Should().BeTrue();

        // Une référence INTERNE à l'agent (adaptateur voisin) ne doit PAS être signalée.
        string internalRef = CombineAndNormalize(
            "agent/src/Liakont.Agent.Core", "..\\Liakont.Agent.Adapters.DemoErpA\\Liakont.Agent.Adapters.DemoErpA.csproj");
        internalRef.Should().Be("agent/src/Liakont.Agent.Adapters.DemoErpA/Liakont.Agent.Adapters.DemoErpA.csproj");
        IsCrossSolution(internalRef).Should().BeFalse();

        // Un nom de paquet (pas un chemin) résolu sous le csproj agent ne s'échappe jamais.
        string packageName = CombineAndNormalize("agent/tests/X", "Newtonsoft.Json");
        IsCrossSolution(packageName).Should().BeFalse();
    }

    private static IEnumerable<string> ComputeEffectivePackages(IEnumerable<string> msbuildXmls)
    {
        var includes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string xml in msbuildXmls)
        {
            foreach (Match m in PackageReferenceInclude.Matches(xml))
            {
                includes.Add(m.Groups[2].Value);
            }

            foreach (Match m in PackageReferenceRemove.Matches(xml))
            {
                removes.Add(m.Groups[2].Value);
            }
        }

        includes.ExceptWith(removes);
        return includes.OrderBy(s => s, StringComparer.Ordinal);
    }

    // Chaîne MSBuild qui s'applique au projet : le csproj lui-même PLUS chaque Directory.Build.props
    // ET Directory.Packages.props rencontrés en remontant jusqu'à la racine du dépôt (inclusivement).
    // Couvre les injections par PackageReference classique (Directory.Build.props) ET par Central
    // Package Management (GlobalPackageReference dans Directory.Packages.props — vecteur CPM).
    // Les entrées <PackageVersion> de Directory.Packages.props sont inoffensives : la regex
    // PackageReferenceInclude ne les reconnaît pas (elle exige PackageReference ou
    // GlobalPackageReference, jamais PackageVersion).
    // Limitation : les chaînes <Import> ne sont PAS suivies (voir doc XML de classe, A5-purity-1).
    private static IEnumerable<string> MsbuildChain(string projectDir, string repoRoot)
    {
        yield return Directory.EnumerateFiles(projectDir, "*.csproj").First();

        DirectoryInfo? dir = new DirectoryInfo(projectDir);
        string repoRootFull = new DirectoryInfo(repoRoot).FullName;
        while (dir != null)
        {
            string buildProps = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(buildProps))
            {
                yield return buildProps;
            }

            string packagesProps = Path.Combine(dir.FullName, "Directory.Packages.props");
            if (File.Exists(packagesProps))
            {
                yield return packagesProps;
            }

            if (string.Equals(dir.FullName, repoRootFull, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            dir = dir.Parent;
        }
    }

    // Combine un répertoire (relatif à la racine du dépôt, séparateurs « / ») avec une valeur Include
    // (séparateurs MSBuild « \ » ou « / », segments « .. »/« . ») et normalise en chemin relatif racine.
    // Purement textuel (aucun accès disque) : tolère les globs (« *.json »).
    private static string CombineAndNormalize(string baseDirRepoRel, string include)
    {
        string combined = (baseDirRepoRel + "/" + include).Replace('\\', '/');
        var segments = new List<string>();
        foreach (string seg in combined.Split('/'))
        {
            if (seg.Length == 0 || seg == ".")
            {
                continue;
            }

            if (seg == "..")
            {
                if (segments.Count > 0 && segments[segments.Count - 1] != "..")
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    segments.Add("..");
                }
            }
            else
            {
                segments.Add(seg);
            }
        }

        return string.Join("/", segments);
    }

    // Un chemin relatif racine est « cross-solution » s'il vise la solution plateforme (src/) ou les
    // tests partagés (tests/) — par opposition à une référence interne sous agent/.
    private static bool IsCrossSolution(string repoRelativePath) =>
        repoRelativePath.StartsWith("src/", StringComparison.OrdinalIgnoreCase) ||
        repoRelativePath.StartsWith("tests/", StringComparison.OrdinalIgnoreCase);

    private static string RepoRelative(string path, string repoRoot)
    {
        string full = new DirectoryInfo(path).FullName.Replace('\\', '/');
        string root = new DirectoryInfo(repoRoot).FullName.Replace('\\', '/').TrimEnd('/') + "/";
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
    }

    private static string AgentRelative(string path, string agentRoot)
    {
        string full = path.Replace('\\', '/');
        string root = new DirectoryInfo(agentRoot).FullName.Replace('\\', '/').TrimEnd('/') + "/";
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
    }

    private static bool IsUnderBinOrObj(string path)
    {
        string n = path.Replace('\\', '/');
        return n.Contains("/bin/") || n.Contains("/obj/");
    }

    // Racine du dépôt = parent de la racine de l'agent (le répertoire portant Liakont.Agent.sln). Robuste
    // au RID/Configuration : remonte depuis la sortie de test (même approche qu'AgentPackageReferenceBoundaryTests).
    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Liakont.Agent.sln")))
            {
                return dir.Parent?.FullName
                    ?? throw new InvalidOperationException("La racine de l'agent (Liakont.Agent.sln) n'a pas de parent.");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Répertoire racine de l'agent (contenant Liakont.Agent.sln) introuvable depuis " + AppContext.BaseDirectory);
    }
}
