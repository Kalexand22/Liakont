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

    [Fact]
    public void Default_Staging_Root_Is_Stable_Outside_The_Build_Tree()
    {
        // FIX07b : sans racine d'instance configurée, le défaut du Host doit être STABLE (App_Data, hors arbre de
        // build) et l'emporter sur le repli AppContext.BaseDirectory (bin/) du module — sinon le contenu stagé est
        // effacé au redéploiement (documents zombies). Ordre de production : module PUIS défaut stable du Host.
        var services = new ServiceCollection();
        var contentRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "liakont-content-" + System.Guid.NewGuid().ToString("N"));

        services.AddStagingModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
        services.AddStableStagingRoot(contentRoot);

        using ServiceProvider provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FileSystemPayloadStagingStoreOptions>>().Value;

        options.RootPath.Should().Be(
            System.IO.Path.Combine(contentRoot, "App_Data", "staging-store"),
            "le défaut stable du Host (hors bin/) doit l'emporter sur le repli BaseDirectory du module");
        options.RootPath.Should().NotStartWith(
            System.AppContext.BaseDirectory,
            "le staging ne doit JAMAIS retomber sous l'arbre de build (bin/) — repli BaseDirectory du module = cause du bug zombie FIX07b");
    }

    [Fact]
    public void Explicit_Staging_Root_Config_Wins_Over_Host_Default()
    {
        // Une racine explicitement configurée (paramétrage d'instance) l'emporte sur le défaut stable du Host.
        var services = new ServiceCollection();
        var configured = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "liakont-volume-dedie");

        services.AddStagingModule(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:Storage:FileSystem:RootPath"] = configured,
            })
            .Build());
        services.AddStableStagingRoot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ignored-content-root"));

        using ServiceProvider provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FileSystemPayloadStagingStoreOptions>>().Value;

        options.RootPath.Should().Be(configured, "la racine configurée par l'instance prime sur le défaut du Host");
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
