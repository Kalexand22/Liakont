namespace Liakont.Host.Tests.Unit.Startup;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Host.Startup;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Liakont.PaClients.Fake;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Câblage du plug-in PA factice au composition root (FIX01d). Avant ce correctif, le Host ne
/// référençait AUCUN plug-in PA : <see cref="IPaClientRegistry"/> ne résolvait rien et l'envoi était
/// inexerçable partout (bug-inbox « plug-in Fake jamais câblé au Host »). Ces tests verrouillent le
/// contrat exact : branché en Development (ou via <c>PaClients:Fake:Enabled</c>), JAMAIS par défaut en
/// production. La résolution se fait par <c>PaType</c> — aucun <c>if (pa is …)</c> (CLAUDE.md n°8).
/// </summary>
public sealed class PaClientBootstrapTests
{
    private const string FakePaType = FakePaClientFactory.PaTypeKey;

    [Fact]
    public void Development_Registers_Fake_PaClient_So_Registry_Resolves_It()
    {
        var registry = BuildRegistry(environment: "Development", fakeEnabledFlag: null);

        registry.IsRegistered(FakePaType).Should().BeTrue(
            "le composition root branche le plug-in factice en Development (FIX01d).");
        registry.RegisteredTypes.Should().Contain(FakePaType);
        registry.Resolve(new PaAccountDescriptor(FakePaType, "dev-tenant"))
            .Should().NotBeNull("le registre doit résoudre un IPaClient pour un compte PA factice câblé.");
    }

    [Fact]
    public void Production_Without_Flag_Does_Not_Register_Fake_PaClient()
    {
        var registry = BuildRegistry(environment: "Production", fakeEnabledFlag: null);

        registry.IsRegistered(FakePaType).Should().BeFalse(
            "le plug-in factice n'est JAMAIS branché par défaut en production (FIX01d).");

        var resolve = () => registry.Resolve(new PaAccountDescriptor(FakePaType, "prod-tenant"));
        resolve.Should().Throw<InvalidOperationException>(
            "sans plug-in enregistré, le registre signale un type PA non résolu (message opérateur).");
    }

    [Fact]
    public void Production_With_Explicit_Flag_Registers_Fake_PaClient()
    {
        var registry = BuildRegistry(environment: "Production", fakeEnabledFlag: true);

        registry.IsRegistered(FakePaType).Should().BeTrue(
            "le drapeau PaClients:Fake:Enabled branche explicitement le plug-in factice hors Development.");
    }

    private static IPaClientRegistry BuildRegistry(string environment, bool? fakeEnabledFlag)
    {
        var configValues = new Dictionary<string, string?>();
        if (fakeEnabledFlag is not null)
        {
            configValues[PaClientBootstrap.EnableFakeConfigKey] = fakeEnabledFlag.Value ? "true" : "false";
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddTransmissionModule();
        services.AddConfiguredPaClients(new StubHostEnvironment(environment), configuration);

        return services.BuildServiceProvider().GetRequiredService<IPaClientRegistry>();
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Liakont.Host.Tests.Unit";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
