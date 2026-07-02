namespace Liakont.Modules.Ged.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Liakont.Modules.Ged.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Vérifie au niveau de l'ARTEFACT que les migrations GED sont réellement embarquées (donc découvertes par le
/// filtre DbUp <c>.Migrations.</c> du <c>MigrationRunner</c> / <c>TenantProvisioningService</c>) et que chaque
/// schéma GED (ged_catalog, ged_index, ged_ingestion) est créé par exactement un script <c>CREATE SCHEMA</c>.
/// Depuis GED03a, le schéma <c>ged_catalog</c> porte ses tables de méta-modèle (entity_types, axis_definitions,
/// axis_values, catalog_change_log) — d'où la garde anti-littéral (INV-GED-12 / règle 7) qui prouve que ces
/// tables restent GÉNÉRIQUES (aucun vocabulaire métier en dur). Les tests base-réelle (ordre FK, append-only,
/// arrondi half-up) sont portés par le projet Tests.Integration (GED03a/b/c, F19 §8).
/// </summary>
public sealed class GedMigrationScaffoldTests
{
    private static readonly Assembly InfrastructureAssembly = typeof(GedModuleRegistration).Assembly;

    private static readonly string[] ExpectedSchemas = ["ged_catalog", "ged_index", "ged_ingestion"];

    // Vocabulaire métier INTERDIT dans les migrations GED (INV-GED-12 / règle 7, F19 §3.3.2) : le méta-modèle
    // est générique — aucun axe / type d'entité métier n'est codé en dur, ils sont du paramétrage tenant
    // (seeds fictifs deployments/). La garde OUTILLÉE complète (tout src/Modules/Ged/** + ci.yml) arrive avec
    // GED11 (RL-27) ; ce test couvre la surface SQL livrée par GED03a (« check anti-littéral vert »).
    private static readonly string[] ForbiddenBusinessVocabulary =
        ["lot", "vente", "pv", "encheres", "enchères", "adjudication", "acheteur", "bordereau"];

    // Numéro de version d'un script de migration embarqué : le préfixe Vnnn du nom de RESSOURCE, capté juste
    // après le segment « .Migrations. » (ex. « …Infrastructure.Migrations.V018__create_ged_… » → 18). DbUp
    // ordonne les scripts par nom de ressource complet : deux scripts « V018__… » distincts coexistent sans
    // erreur (schémas disjoints), mais la collision de numéros entre items parallèles est un piège récurrent
    // (V017 GED05b↔GED13, V018 GED24↔GED05b) jusque-là géré par la seule discipline humaine.
    private static readonly Regex MigrationNumberPattern = new(@"\.Migrations\.V(\d+)__", RegexOptions.Compiled);

    [Fact]
    public void Each_ged_schema_is_created_by_exactly_one_embedded_migration()
    {
        var migrations = LoadMigrationScripts();

        var schemaCreationScripts = migrations.Values
            .Count(sql => Contains(sql, "CREATE SCHEMA IF NOT EXISTS"));

        var countReason =
            "chaque schéma GED (ged_catalog, ged_index, ged_ingestion) est créé par exactement un script "
            + "CREATE SCHEMA (les tables du méta-modèle vivent dans des migrations séparées) ; scripts "
            + $"embarqués trouvés : {string.Join(", ", migrations.Keys)}";
        schemaCreationScripts.Should().Be(ExpectedSchemas.Length, countReason);

        foreach (var schema in ExpectedSchemas)
        {
            migrations.Values
                .Count(sql => Contains(sql, $"CREATE SCHEMA IF NOT EXISTS {schema}"))
                .Should()
                .Be(1, "exactement un script crée le schéma {0} (idempotent)", schema);
        }
    }

    [Fact]
    public void Ged_migrations_hardcode_no_business_vocabulary()
    {
        var migrations = LoadMigrationScripts();

        var offenders = new List<string>();
        foreach (var (resource, sql) in migrations)
        {
            foreach (var word in ForbiddenBusinessVocabulary)
            {
                if (Regex.IsMatch(sql, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
                {
                    offenders.Add($"{resource} → « {word} »");
                }
            }
        }

        var reason =
            "le méta-modèle GED est générique (INV-GED-12 / règle 7) : aucun axe / type d'entité métier n'est "
            + $"codé en dur dans une migration (paramétrage tenant, seeds deployments/) — fautifs : {string.Join(" ; ", offenders)}";
        offenders.Should().BeEmpty(reason);
    }

    [Fact]
    public void AddGedModule_registers_the_infrastructure_assembly_for_migrations()
    {
        var services = new ServiceCollection();
        services.AddGedModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MigrationAssembliesOptions>>().Value;

        var reason =
            "sans déclaration de l'assembly d'Infrastructure dans MigrationAssembliesOptions, les schémas GED "
            + "ne seraient jamais appliqués par le runner DbUp";
        options.Assemblies.Should().Contain(InfrastructureAssembly, reason);
    }

    [Fact]
    public void Ged_migrations_have_no_duplicate_version_number()
    {
        var resourceNames = LoadMigrationScripts().Keys;

        // Anti-faux-vert : si l'extracteur ne captait AUCUN numéro (regex/format de ressource cassé), la
        // recherche de doublons rendrait vide et le test passerait à vide. On exige d'abord que le préfixe
        // Vnnn soit reconnu sur une part réaliste des scripts embarqués (marche normale : 20 scripts GED).
        var parsedNumbers = resourceNames
            .Select(ExtractMigrationNumber)
            .Where(number => number.HasValue)
            .ToList();
        parsedNumbers.Count.Should().BeGreaterThan(10, "l'extracteur de numéro doit reconnaître les scripts GED embarqués (sinon garde à vide)");

        var duplicates = FindDuplicateMigrationNumbers(resourceNames);

        var reason =
            "deux migrations du même module ne doivent pas partager un numéro Vnnn : DbUp tolère la coexistence "
            + "(ordre = nom de ressource complet, schémas disjoints), mais la collision entre items parallèles est "
            + "un piège récurrent (V017 GED05b↔GED13, V018 GED24↔GED05b) — renuméroter le doublon ; "
            + $"numéros en double : {string.Join(", ", duplicates.Select(number => $"V{number:D3}"))}";
        duplicates.Should().BeEmpty(reason);
    }

    [Fact]
    public void Duplicate_migration_number_detector_flags_a_collision()
    {
        // Self-test du garde-fou lui-même (RL-27) : le détecteur DOIT lever un doublon injecté, sinon la garde
        // ci-dessus serait un faux-vert. Deux ressources « V021__… » distinctes → le numéro 21 est signalé ;
        // le numéro unique 22 ne l'est pas.
        var resourceNames = new[]
        {
            "Liakont.Modules.Ged.Infrastructure.Migrations.V021__alpha.sql",
            "Liakont.Modules.Ged.Infrastructure.Migrations.V021__beta.sql",
            "Liakont.Modules.Ged.Infrastructure.Migrations.V022__gamma.sql",
        };

        FindDuplicateMigrationNumbers(resourceNames).Should().Equal(21);
    }

    /// <summary>
    /// Extrait le numéro de version <c>Vnnn</c> d'un nom de ressource de migration embarquée, ou <c>null</c>
    /// si le nom ne porte pas le préfixe attendu (fichier hors convention).
    /// </summary>
    private static int? ExtractMigrationNumber(string resourceName)
    {
        var match = MigrationNumberPattern.Match(resourceName);
        return match.Success
            ? int.Parse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Rend les numéros de version partagés par au moins deux scripts de migration (triés croissant). Vide
    /// = aucun doublon. Base commune de la garde réelle et de son self-test.
    /// </summary>
    private static List<int> FindDuplicateMigrationNumbers(IEnumerable<string> resourceNames) =>
        resourceNames
            .Select(ExtractMigrationNumber)
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .GroupBy(number => number)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(number => number)
            .ToList();

    /// <summary>
    /// Charge les scripts de migration embarqués tels que DbUp les découvre : ressources dont le nom contient
    /// <c>.Migrations.</c> et se termine par <c>.sql</c> (même filtre que <c>MigrationRunner</c>).
    /// </summary>
    private static Dictionary<string, string> LoadMigrationScripts()
    {
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var resourceName in InfrastructureAssembly.GetManifestResourceNames())
        {
            if (!resourceName.Contains(".Migrations.", StringComparison.Ordinal)
                || !resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = InfrastructureAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Ressource de migration illisible : {resourceName}.");
            using var reader = new StreamReader(stream);
            scripts[resourceName] = reader.ReadToEnd();
        }

        return scripts;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
