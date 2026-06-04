namespace Liakont.Modules.Ingestion.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Ingestion.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Garde anti faux-vert : vérifie que la CHAÎNE D'ENREGISTREMENT réelle de production
/// (<c>AddIngestionModule</c> → <c>Configure&lt;MigrationAssembliesOptions&gt;</c>) enrôle bien
/// l'assembly du module pour la migration. Sans ce test, retirer cette ligne casserait la création
/// des tables <c>ingestion.*</c> en production sans faire échouer les tests d'intégration (qui, par
/// convention du dépôt, appliquent les migrations du module en direct via DbUp dans la fixture).
/// </summary>
public sealed class IngestionModuleRegistrationTests
{
    [Fact]
    public void AddIngestionModule_Enrolls_Module_Assembly_For_Migrations()
    {
        var services = new ServiceCollection();

        services.AddIngestionModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MigrationAssembliesOptions>>().Value;

        options.Assemblies.Should().Contain(typeof(IngestionModuleRegistration).Assembly);
    }
}
