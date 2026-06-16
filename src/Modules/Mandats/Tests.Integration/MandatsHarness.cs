namespace Liakont.Modules.Mandats.Tests.Integration;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure;
using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Assemble les vraies dépendances de persistance du module sur le conteneur de test, via la VRAIE composition
/// DI de production (AddDocumentApprovalModule + AddMandatsModule). Depuis SIG05, l'acceptation 389 est PROJETÉE
/// via DocumentApproval : la companion de persistance (état + journal append-only) est le module générique,
/// piloté par <see cref="ISelfBilledAcceptanceCommands"/> et lu par <see cref="ISelfBilledAcceptanceQueries"/>.
/// Résoudre par les CONTRATS publics (jamais les classes internes d'un autre module) ; les tests pilotent ces
/// ports avec des <c>company_id</c> explicites (tenant-scoping prouvé sur ≥ 2 sociétés).
/// </summary>
internal sealed class MandatsHarness
{
    public MandatsHarness(MandatsDatabaseFixture fixture)
        : this(fixture.CreateConnectionFactory())
    {
    }

    public MandatsHarness(IConnectionFactory connectionFactory)
    {
        ConnectionFactory = connectionFactory;
        var provider = BuildProvider(connectionFactory);

        UowFactory = provider.GetRequiredService<IMandatsUnitOfWorkFactory>();
        Queries = provider.GetRequiredService<IMandatsQueries>();
        ApprovalQueries = provider.GetRequiredService<IDocumentApprovalQueries>();
        Workflow = provider.GetRequiredService<IDocumentApprovalWorkflow>();
        Commands = provider.GetRequiredService<ISelfBilledAcceptanceCommands>();
        AcceptanceQueries = provider.GetRequiredService<ISelfBilledAcceptanceQueries>();
        NumberAllocator = provider.GetRequiredService<ISelfBilledNumberAllocator>();
    }

    public IConnectionFactory ConnectionFactory { get; }

    public IMandatsUnitOfWorkFactory UowFactory { get; }

    public IMandatsQueries Queries { get; }

    public IDocumentApprovalQueries ApprovalQueries { get; }

    public IDocumentApprovalWorkflow Workflow { get; }

    public ISelfBilledAcceptanceCommands Commands { get; }

    public ISelfBilledAcceptanceQueries AcceptanceQueries { get; }

    public ISelfBilledNumberAllocator NumberAllocator { get; }

    /// <summary>
    /// Compose les modules DocumentApproval + Mandats sur une fabrique de connexion donnée et renvoie le
    /// fournisseur racine. Les services sont scoped ; on résout depuis la racine (sans validation de scope) car
    /// chaque UoW/requête ouvre sa propre connexion — comportement adapté à un harnais de test.
    /// </summary>
    public static ServiceProvider BuildProvider(IConnectionFactory connectionFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(connectionFactory);
        services.AddDocumentApprovalModule();
        services.AddMandatsModule();
        return services.BuildServiceProvider();
    }
}
