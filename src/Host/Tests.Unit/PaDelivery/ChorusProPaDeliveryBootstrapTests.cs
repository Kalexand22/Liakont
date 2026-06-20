namespace Liakont.Host.Tests.Unit.PaDelivery;

using System;
using FluentAssertions;
using Liakont.Host.PaDelivery;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Liakont.PaClients.ChorusPro;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Câblage Host du plug-in Chorus Pro (CP07) : <see cref="ChorusProPaDeliveryBootstrap.AddChorusProPaDelivery"/>
/// enregistre le résolveur de compte Host (frontière coffre du tenant) AVANT la fabrique, de sorte que le
/// registre du module Transmission résolve un <see cref="IPaClient"/> par <c>PaType « ChorusPro »</c> — aucun
/// <c>if (pa is …)</c> (CLAUDE.md n°6/8/16). Le câblage doit vivre HORS de
/// <c>PaClientBootstrap.AddConfiguredPaClients</c> (qui ne branche que le Fake).
/// </summary>
public sealed class ChorusProPaDeliveryBootstrapTests
{
    [Fact]
    public void AddChorusProPaDelivery_Registers_Host_Resolver_And_Factory()
    {
        var services = new ServiceCollection();

        services.AddChorusProPaDelivery();

        // Inscriptions inspectées sur les descripteurs (sans instancier : le résolveur exige
        // ITenantScopeFactory + ISecretProtector, fournis seulement par l'assemblage complet du Host).
        services.Should().ContainSingle(d => d.ServiceType == typeof(IChorusProAccountResolver))
            .Which.ImplementationType.Should().Be<ChorusProAccountResolver>(
                "le bootstrap enregistre le résolveur Host (déchiffrement des secrets du coffre du tenant), AVANT la fabrique.");

        services.Should().Contain(
            d => d.ServiceType == typeof(IPaClientFactory) && d.ImplementationType == typeof(ChorusProClientFactory),
            "la fabrique Chorus Pro est ajoutée à l'ensemble des IPaClientFactory que le registre découvre par clé.");
    }

    [Fact]
    public void Registry_Resolves_ChorusPro_Client_By_PaType()
    {
        var services = new ServiceCollection();
        services.AddTransmissionModule();

        // Le résolveur réel déchiffre le coffre du tenant : on le remplace par un stub (TryAddSingleton du
        // bootstrap n'écrase pas une inscription préalable) pour exercer la résolution par le registre sans
        // infrastructure tenant. La fabrique reste la vraie (câblée par AddChorusProPaDelivery).
        services.AddSingleton<IChorusProAccountResolver, FixedConfigResolver>();
        services.AddChorusProPaDelivery();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPaClientRegistry>();

        var client = registry.Resolve(new PaAccountDescriptor(ChorusProDefaults.PaTypeKey, "tenant-1"));

        client.Should().NotBeNull("le registre doit résoudre un IPaClient pour un compte Chorus Pro câblé.");
    }

    private sealed class FixedConfigResolver : IChorusProAccountResolver
    {
        private static readonly ChorusProAccountConfig Config = new(
            ChorusProEnvironment.Qualification,
            new Uri("https://sandbox-api.piste.gouv.fr/cpro/", UriKind.Absolute),
            new Uri("https://sandbox-oauth.piste.gouv.fr/api/oauth/token", UriKind.Absolute),
            accountId: "ACC-FICTIF",
            pisteClientId: "client-FICTIF",
            pisteClientSecret: "secret-FICTIF",
            technicalLogin: "login-FICTIF",
            technicalPassword: "mdp-FICTIF",
            connectionEmail: "tech-FICTIF@example.test");

        public ChorusProAccountConfig Resolve(PaAccountDescriptor account) => Config;
    }
}
