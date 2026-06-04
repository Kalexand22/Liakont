namespace Liakont.Modules.Documents.Tests.Integration;

using System.Linq;
using FluentAssertions;
using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Documents.Infrastructure.Queries;
using Liakont.Modules.Ingestion.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Garde anti faux-vert : la CHAÎNE D'ENREGISTREMENT réelle de production (<c>AddDocumentsModule</c>)
/// enrôle l'assembly du module pour la migration et branche le port d'ingestion sur la vraie
/// implémentation (et non plus le no-op). Sans ce test, retirer une ligne casserait la création des
/// tables <c>documents.*</c> ou le câblage <c>IDocumentIntake</c> en production sans faire échouer les
/// tests d'intégration (qui appliquent les migrations en direct via DbUp dans la fixture).
/// </summary>
public sealed class DocumentsModuleRegistrationTests
{
    [Fact]
    public void AddDocumentsModule_Enrolls_Module_Assembly_For_Migrations()
    {
        var services = new ServiceCollection();

        services.AddDocumentsModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MigrationAssembliesOptions>>().Value;

        options.Assemblies.Should().Contain(typeof(DocumentsModuleRegistration).Assembly);
    }

    [Fact]
    public void AddDocumentsModule_Registers_Persistence_And_Intake_Port()
    {
        var services = new ServiceCollection();

        services.AddDocumentsModule();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDocumentUnitOfWorkFactory) &&
            d.ImplementationType == typeof(PostgresDocumentUnitOfWorkFactory));

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDocumentQueries) &&
            d.ImplementationType == typeof(PostgresDocumentQueries));

        // Le port de création du document est branché sur la VRAIE implémentation (pas le no-op).
        services.Should().ContainSingle(d => d.ServiceType == typeof(IDocumentIntake))
            .Which.ImplementationType.Should().Be<DocumentIntake>();
    }

    [Fact]
    public void AddDocumentsModule_Replace_Overrides_Any_Prior_IDocumentIntake_Registration()
    {
        // Simule un enregistrement préalable (ex. NoOpDocumentIntake du module Ingestion).
        var services = new ServiceCollection();
        services.AddScoped<IDocumentIntake, StubIntake>();

        services.AddDocumentsModule();

        // Replace doit écraser le stub : un seul descripteur, pointant sur la vraie implémentation.
        services.Should().ContainSingle(d => d.ServiceType == typeof(IDocumentIntake))
            .Which.ImplementationType.Should().Be<DocumentIntake>();
    }

    private sealed class StubIntake : IDocumentIntake
    {
        public System.Threading.Tasks.Task RegisterDetectedDocumentAsync(
            Liakont.Modules.Ingestion.Contracts.DetectedDocumentIntake input,
            System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.CompletedTask;
    }
}
