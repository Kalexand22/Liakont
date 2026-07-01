namespace Liakont.Modules.Ged.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Liakont.Modules.Ged.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Vérifie l'acceptance GED02 « trois schémas PG créés VIDES par migration, aucune table métier » au niveau
/// de l'ARTEFACT : les scripts sont réellement embarqués (donc découverts par le filtre DbUp
/// <c>.Migrations.</c> du <c>MigrationRunner</c> / <c>TenantProvisioningService</c>), créent chacun leur
/// schéma, et ne créent AUCUNE table à ce stade (les tables du méta-modèle arrivent avec GED03+). Les tests
/// base-réelle (append-only, ordre FK, etc.) sont portés par les items GED03a/b/c (F19 §8).
/// </summary>
public sealed class GedMigrationScaffoldTests
{
    private static readonly Assembly InfrastructureAssembly = typeof(GedModuleRegistration).Assembly;

    private static readonly string[] ExpectedSchemas = ["ged_catalog", "ged_index", "ged_ingestion"];

    [Fact]
    public void The_three_ged_schemas_are_created_by_embedded_migrations()
    {
        var migrations = LoadMigrationScripts();

        var countReason =
            "GED02 livre exactement un script de création de schéma par schéma GED (ged_catalog, ged_index, "
            + $"ged_ingestion) ; scripts embarqués trouvés : {string.Join(", ", migrations.Keys)}";
        migrations.Should().HaveCount(ExpectedSchemas.Length, countReason);

        foreach (var schema in ExpectedSchemas)
        {
            migrations.Values
                .Count(sql => Contains(sql, $"CREATE SCHEMA IF NOT EXISTS {schema}"))
                .Should()
                .Be(1, "exactement un script crée le schéma {0} (idempotent)", schema);
        }
    }

    [Fact]
    public void No_ged_migration_creates_a_business_table_yet()
    {
        var migrations = LoadMigrationScripts();

        var offenders = migrations
            .Where(kvp => Contains(kvp.Value, "CREATE TABLE"))
            .Select(kvp => kvp.Key)
            .ToList();

        var reason =
            "acceptance GED02 : les schémas sont créés VIDES, AUCUNE table métier (les tables du méta-modèle "
            + $"arrivent avec GED03a/b/c) — scripts fautifs : {string.Join(", ", offenders)}";
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
