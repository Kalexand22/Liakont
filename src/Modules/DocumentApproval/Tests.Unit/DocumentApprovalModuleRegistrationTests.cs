namespace Liakont.Modules.DocumentApproval.Tests.Unit;

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Application;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Smoke-test du graphe DI du module DocumentApproval (SIG04) : <c>AddDocumentApprovalModule</c> rend
/// RÉSOLVABLES les contrats publics ET enregistre l'assembly de migrations dans
/// <see cref="MigrationAssembliesOptions"/> — sinon le schéma <c>documentapproval</c> ne serait jamais déployé
/// au démarrage (faux-vert : les tests d'intégration appliquent les scripts eux-mêmes). Un oubli de câblage
/// échoue ici plutôt qu'au runtime du Host.
/// </summary>
public sealed class DocumentApprovalModuleRegistrationTests
{
    [Fact]
    public void AddDocumentApprovalModule_Resolves_Public_Contracts_And_Registers_Migration_Assembly()
    {
        var services = new ServiceCollection();
        services.AddScoped<IConnectionFactory, FakeConnectionFactory>();

        services.AddDocumentApprovalModule();

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        sp.GetRequiredService<IDocumentApprovalQueries>().Should().NotBeNull(
            "IDocumentApprovalQueries doit être résolu après AddDocumentApprovalModule");
        sp.GetRequiredService<IDocumentValidationUnitOfWorkFactory>().Should().NotBeNull(
            "IDocumentValidationUnitOfWorkFactory doit être résolu après AddDocumentApprovalModule");

        var migrationOptions = sp.GetRequiredService<IOptions<MigrationAssembliesOptions>>().Value;
        migrationOptions.Assemblies.Should().Contain(
            typeof(DocumentApprovalModuleRegistration).Assembly,
            "l'assembly de migrations DocumentApproval doit être enregistré, sinon le schéma `documentapproval` n'est jamais déployé au démarrage");
    }

    private sealed class FakeConnectionFactory : IConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Le smoke-test ne résout que le graphe DI, il n'ouvre pas de connexion.");
    }
}
