namespace Liakont.Modules.Pipeline.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.Pipeline.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Garde anti faux-vert : la CHAÎNE D'ENREGISTREMENT réelle de production (<c>AddPipelineModule</c>) enrôle
/// l'assembly du module pour la migration et branche la lecture du journal d'exécutions. Sans ce test,
/// retirer la ligne <c>Configure&lt;MigrationAssembliesOptions&gt;</c> casserait la création de
/// <c>pipeline.run_logs</c> en production SANS faire échouer les tests d'intégration (qui appliquent la
/// migration en direct via DbUp dans la fixture).
/// </summary>
public sealed class PipelineModuleRegistrationTests
{
    [Fact]
    public void AddPipelineModule_Enrolls_Module_Assembly_For_Migrations()
    {
        var services = new ServiceCollection();

        services.AddPipelineModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MigrationAssembliesOptions>>().Value;

        options.Assemblies.Should().Contain(typeof(PipelineModuleRegistration).Assembly);
    }

    [Fact]
    public void AddPipelineModule_Registers_Run_Log_Queries()
    {
        var services = new ServiceCollection();

        services.AddPipelineModule();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineRunQueries) &&
            d.ImplementationType == typeof(PostgresPipelineRunQueries));
    }
}
