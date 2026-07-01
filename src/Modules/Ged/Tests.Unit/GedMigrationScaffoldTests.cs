namespace Liakont.Modules.Ged.Tests.Unit;

using System;
using System.Collections.Generic;
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
