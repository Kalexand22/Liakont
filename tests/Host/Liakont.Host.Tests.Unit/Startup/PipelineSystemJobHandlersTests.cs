namespace Liakont.Host.Tests.Unit.Startup;

using System;
using FluentAssertions;
using Liakont.Host.Startup;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts.Services;
using Stratum.Modules.Job.Infrastructure;
using Xunit;

/// <summary>
/// RDL06 (findings A6-cons-1 P1 / A6-cons-3 P2) — test de composition de l'EXTENSION
/// <c>AddPipelineSystemJobHandlers</c> : les fan-out SYSTÈME récurrents du pipeline (SendAll / SyncAll /
/// AggregatePaymentsAll / RectifyReportsAll / AggregateB2cMarginAll / AggregateB2cTaxableAll /
/// AggregateB2cExportAll / AggregateB2cPlainTaxableAll) doivent être à la fois DISPATCHABLES
/// (<c>IJobHandlerResolver.CanHandle</c>) et PLANIFIABLES (présents dans <c>IJobTypeCatalog</c>). C'est
/// l'enregistrement <c>AddJobHandler</c> (et la <c>JobHandlerRegistration</c> singleton qu'il pose) qui le
/// garantit — un <c>AddScoped</c> seul (état antérieur dans le module Pipeline) laissait ces déclencheurs
/// muets pour le resolver et le catalogue (jobs morts en production).
/// <para>
/// PORTÉE : ce test couvre la CORRECTION de l'extension, pas son CÂBLAGE au composition root. La garde du
/// câblage réel (si <c>AddPipelineSystemJobHandlers</c> est retiré d'<c>AppBootstrap</c>, les jobs
/// redeviennent morts) est portée par le test d'INTÉGRATION sur le Host réel
/// (<c>PipelineSystemJobHandlersCompositionIntegrationTests</c>), qui résout le resolver/catalogue depuis le
/// graphe DI de production.
/// </para>
/// </summary>
public sealed class PipelineSystemJobHandlersTests
{
    // Les déclencheurs de fan-out récurrents câblés par AddPipelineSystemJobHandlers (doit refléter l'extension —
    // tout nouveau handler enregistré doit être ajouté ici ; la garde drift-proof réelle est portée par
    // PipelineSystemJobHandlersCompositionIntegrationTests, qui dérive la liste de l'extension).
    private static readonly Type[] RecurringFanOutTriggers =
    [
        typeof(SendAllTrigger),
        typeof(SyncAllTrigger),
        typeof(AggregatePaymentsAllTrigger),
        typeof(RectifyReportsAllTrigger),
        typeof(AggregateB2cMarginAllTrigger),
        typeof(AggregateB2cTaxableAllTrigger),
        typeof(AggregateB2cExportAllTrigger),
        typeof(AggregateB2cPlainTaxableAllTrigger),
    ];

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        // Graphe RÉEL : le module Job fournit IJobHandlerResolver + IJobTypeCatalog (construits depuis les
        // JobHandlerRegistration singletons), AddPipelineSystemJobHandlers est l'extension exacte appelée par
        // le composition root du Host (AppBootstrap).
        services.AddJobModule(configuration);
        services.AddPipelineSystemJobHandlers();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Each_Recurring_FanOut_Trigger_Is_Dispatchable_And_Schedulable()
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<IJobHandlerResolver>();
        var catalog = provider.GetRequiredService<IJobTypeCatalog>();

        foreach (var trigger in RecurringFanOutTriggers)
        {
            var key = trigger.FullName!;

            resolver.CanHandle(key).Should()
                .BeTrue($"{trigger.Name} doit être dispatchable (JobHandlerRegistration présente)");

            catalog.Find(key).Should()
                .NotBeNull($"{trigger.Name} doit être planifiable (présent dans le catalogue des types de jobs)");
        }
    }

    [Fact]
    public void Each_Recurring_FanOut_Trigger_Is_Listed_In_The_Catalog()
    {
        using var provider = BuildProvider();
        var catalog = provider.GetRequiredService<IJobTypeCatalog>();

        var keys = catalog.GetAll();

        foreach (var trigger in RecurringFanOutTriggers)
        {
            keys.Should().Contain(d => d.TechnicalKey == trigger.FullName, $"{trigger.Name} doit apparaître dans GetAll()");
        }
    }

    [Fact]
    public void Each_Recurring_FanOut_Trigger_Has_A_French_Label_Not_The_FullName()
    {
        using var provider = BuildProvider();
        var catalog = provider.GetRequiredService<IJobTypeCatalog>();

        foreach (var trigger in RecurringFanOutTriggers)
        {
            var descriptor = catalog.Find(trigger.FullName!);

            descriptor.Should().NotBeNull();
            descriptor!.DisplayName.Should().NotBeNullOrWhiteSpace();
            descriptor.DisplayName.Should().NotBe(trigger.FullName, "le libellé exposé à l'opérateur est en français, jamais le FullName .NET");
        }
    }
}
