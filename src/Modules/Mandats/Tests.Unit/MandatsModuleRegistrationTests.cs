namespace Liakont.Modules.Mandats.Tests.Unit;

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Smoke-test du graphe DI du module Mandats (MND01) : avec la connexion fournie par le Host (ici un
/// double minimal), <c>AddMandatsModule</c> doit rendre RÉSOLVABLES les contrats publics ET enregistrer
/// l'assembly de migrations dans <see cref="MigrationAssembliesOptions"/> — sinon le schéma <c>mandats</c>
/// ne serait jamais déployé au démarrage de l'instance (faux-vert : les tests d'intégration appliquent
/// les scripts eux-mêmes). Un oubli de câblage échoue ici plutôt qu'au runtime du Host.
/// </summary>
public sealed class MandatsModuleRegistrationTests
{
    [Fact]
    public void AddMandatsModule_Resolves_Public_Contracts_And_Registers_Migration_Assembly()
    {
        var services = new ServiceCollection();
        services.AddScoped<IConnectionFactory, FakeConnectionFactory>();

        services.AddMandatsModule();

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        sp.GetRequiredService<IMandatsQueries>().Should().NotBeNull("IMandatsQueries doit être résolu après AddMandatsModule");
        sp.GetRequiredService<IMandatsUnitOfWorkFactory>().Should().NotBeNull("IMandatsUnitOfWorkFactory doit être résolu après AddMandatsModule");
        sp.GetRequiredService<ISelfBilledAcceptanceQueries>().Should().NotBeNull("ISelfBilledAcceptanceQueries (MND02) doit être résolu après AddMandatsModule");
        sp.GetRequiredService<ISelfBilledAcceptanceUnitOfWorkFactory>().Should().NotBeNull("ISelfBilledAcceptanceUnitOfWorkFactory (MND02) doit être résolu après AddMandatsModule");
        sp.GetRequiredService<ISelfBilledGate>().Should().NotBeNull("ISelfBilledGate (MND03) doit être résolu après AddMandatsModule");
        sp.GetRequiredService<ITacitAcceptanceService>().Should().NotBeNull("ITacitAcceptanceService (MND04) doit être résolu après AddMandatsModule — sinon le job de bascule tacite ne tourne pas");

        var migrationOptions = sp.GetRequiredService<IOptions<MigrationAssembliesOptions>>().Value;
        migrationOptions.Assemblies.Should().Contain(
            typeof(MandatsModuleRegistration).Assembly,
            "l'assembly de migrations Mandats doit être enregistré, sinon le schéma `mandats` n'est jamais déployé au démarrage");
    }

    private sealed class FakeConnectionFactory : IConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default) =>
            throw new System.NotSupportedException("Le smoke-test ne résout que le graphe DI, il n'ouvre pas de connexion.");
    }
}
