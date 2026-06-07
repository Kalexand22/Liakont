namespace Liakont.Host.Tests.Unit.Staging;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Host.Staging;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Staging.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Vérifie que le câblage DI du module Staging (PIP00, ADR-0014) + la sonde de présence WORM câblée au
/// Host résolvent bien les trois contrats publics sans erreur de graphe de dépendances.
/// </summary>
public sealed class StagingModuleRegistrationTests
{
    [Fact]
    public void AddStagingModule_And_Probe_Resolve_All_Contracts()
    {
        var services = new ServiceCollection();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Module Staging (IPayloadStagingStore + IStagingPurgeService).
        services.AddStagingModule(configuration);

        // Sonde de présence WORM câblée au Host (dépend de IArchiveStore + ITenantContext).
        services.AddScoped<IArchivedDocumentProbe, ArchiveStoreArchivedDocumentProbe>();

        // Dépendances minimales de la sonde et du store.
        services.AddScoped<IArchiveStore>(_ =>
            new FileSystemArchiveStore(
                Microsoft.Extensions.Options.Options.Create(
                    new FileSystemArchiveStoreOptions { RootPath = System.IO.Path.GetTempPath() })));

        services.AddScoped<ITenantContext>(_ => new StubTenantContext("tenant-di-test"));

        // Data Protection : requis par FileSystemPayloadStagingStore (chiffrement au repos).
        services.AddDataProtection();

        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        using IServiceScope scope = provider.CreateScope();

        var stagingStore = scope.ServiceProvider.GetService<IPayloadStagingStore>();
        var purgeService = scope.ServiceProvider.GetService<IStagingPurgeService>();
        var probe = scope.ServiceProvider.GetService<IArchivedDocumentProbe>();

        stagingStore.Should().NotBeNull("IPayloadStagingStore doit être résolu après AddStagingModule");
        purgeService.Should().NotBeNull("IStagingPurgeService doit être résolu après AddStagingModule");
        probe.Should().NotBeNull("IArchivedDocumentProbe doit être résolu après l'enregistrement de la sonde au Host");
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(string tenantId)
        {
            TenantId = tenantId;
        }

        public string? TenantId { get; }

        public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
    }
}
