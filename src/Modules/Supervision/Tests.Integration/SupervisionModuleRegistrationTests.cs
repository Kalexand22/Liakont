namespace Liakont.Modules.Supervision.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Garde anti faux-vert : la CHAÎNE D'ENREGISTREMENT réelle de production (<c>AddSupervisionModule</c>)
/// enrôle l'assembly du module pour la migration et branche le store, les lectures, le moteur et
/// l'acquittement. Sans ce test, retirer la ligne d'enrôlement casserait la création du schéma
/// <c>supervision</c> et de la table <c>alerts</c> en production sans faire échouer les tests d'intégration
/// (qui appliquent les migrations en direct via DbUp dans la fixture, contournant <c>AddSupervisionModule</c>).
/// </summary>
public sealed class SupervisionModuleRegistrationTests
{
    [Fact]
    public void AddSupervisionModule_Enrolls_Module_Assembly_For_Migrations()
    {
        var services = new ServiceCollection();

        services.AddSupervisionModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MigrationAssembliesOptions>>().Value;

        options.Assemblies.Should().Contain(typeof(SupervisionModuleRegistration).Assembly);
    }

    [Fact]
    public void AddSupervisionModule_Registers_Store_Queries_Engine_And_Acknowledgement()
    {
        var services = new ServiceCollection();

        services.AddSupervisionModule();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IAlertStore) &&
            d.ImplementationType == typeof(PostgresAlertStore));

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IAlertQueries) &&
            d.ImplementationType == typeof(PostgresAlertQueries));

        // Acquittement et moteur sont enregistrés via une fabrique (pas d'ImplementationType) : on vérifie
        // que le type de service est bien enrôlé une seule fois.
        services.Should().ContainSingle(d => d.ServiceType == typeof(IAlertAcknowledgementService));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IAlertEvaluationService));
    }
}
