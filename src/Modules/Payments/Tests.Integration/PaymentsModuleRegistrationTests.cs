namespace Liakont.Modules.Payments.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Payments.Application;
using Liakont.Modules.Payments.Contracts.Queries;
using Liakont.Modules.Payments.Infrastructure;
using Liakont.Modules.Payments.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Garde anti faux-vert : la CHAÎNE D'ENREGISTREMENT réelle de production (<c>AddPaymentsModule</c>) enrôle
/// l'assembly du module pour la migration et branche la persistance (UoW + requêtes). Sans ce test, retirer
/// une ligne casserait la création des tables <c>payments.*</c> en production sans faire échouer les tests
/// d'intégration (qui appliquent les migrations en direct via DbUp dans la fixture).
/// </summary>
public sealed class PaymentsModuleRegistrationTests
{
    [Fact]
    public void AddPaymentsModule_Enrolls_Module_Assembly_For_Migrations()
    {
        var services = new ServiceCollection();

        services.AddPaymentsModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MigrationAssembliesOptions>>().Value;

        options.Assemblies.Should().Contain(typeof(PaymentsModuleRegistration).Assembly);
    }

    [Fact]
    public void AddPaymentsModule_Registers_Persistence()
    {
        var services = new ServiceCollection();

        services.AddPaymentsModule();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPaymentUnitOfWorkFactory) &&
            d.ImplementationType == typeof(PostgresPaymentUnitOfWorkFactory));

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPaymentQueries) &&
            d.ImplementationType == typeof(PostgresPaymentQueries));
    }
}
