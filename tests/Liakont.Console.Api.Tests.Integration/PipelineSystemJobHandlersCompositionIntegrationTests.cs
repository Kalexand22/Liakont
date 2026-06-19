namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Host.Startup;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts.Services;
using Stratum.Modules.Job.Infrastructure;
using Xunit;

/// <summary>
/// RDL06 (finding A6-cons-3) — garde de CÂBLAGE sur le graphe DI de PRODUCTION : les fan-out SYSTÈME
/// récurrents du pipeline doivent être dispatchables (<c>IJobHandlerResolver.CanHandle</c>) et planifiables
/// (<c>IJobTypeCatalog</c>) dans le Host RÉEL (<see cref="ConsoleApiFactory"/>, construit via
/// <c>AppBootstrap.ConfigureServices</c>). Complément du test unitaire de l'extension : si quelqu'un retire
/// l'appel <c>AddPipelineSystemJobHandlers()</c> du composition root, les jobs redeviennent muets pour le
/// resolver/catalogue (jobs morts en production) et CE test passe au rouge — ce que le test unitaire de
/// l'extension en isolation ne couvre pas.
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class PipelineSystemJobHandlersCompositionIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public PipelineSystemJobHandlersCompositionIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Real_Host_Dispatches_And_Schedules_Every_Pipeline_FanOut_Trigger()
    {
        var resolver = _factory.Services.GetRequiredService<IJobHandlerResolver>();
        var catalog = _factory.Services.GetRequiredService<IJobTypeCatalog>();

        var payloadTypes = RecurringFanOutPayloadTypes();
        payloadTypes.Should().NotBeEmpty("AddPipelineSystemJobHandlers doit enregistrer au moins un fan-out");

        foreach (var trigger in payloadTypes)
        {
            var key = trigger.FullName!;

            resolver.CanHandle(key).Should()
                .BeTrue($"{trigger.Name} doit être dispatchable dans le Host réel (AppBootstrap câble AddPipelineSystemJobHandlers)");

            catalog.Find(key).Should()
                .NotBeNull($"{trigger.Name} doit être planifiable dans le Host réel (présent au catalogue des types de jobs)");
        }
    }

    /// <summary>
    /// Liste des payloads de fan-out récurrents DÉRIVÉE de l'extension elle-même (drift-proof) : un fan-out
    /// ajouté à <c>AddPipelineSystemJobHandlers</c> est automatiquement couvert, sans liste maintenue à la main.
    /// </summary>
    private static List<Type> RecurringFanOutPayloadTypes()
    {
        var probe = new ServiceCollection();
        probe.AddPipelineSystemJobHandlers();

        return probe
            .Where(d => d.ServiceType == typeof(JobHandlerRegistration) && d.ImplementationInstance is JobHandlerRegistration)
            .Select(d => ((JobHandlerRegistration)d.ImplementationInstance!).PayloadType)
            .ToList();
    }
}
