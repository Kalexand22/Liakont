namespace Liakont.Modules.Transmission.Tests.Unit;

using System.Reflection;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière de l'abstraction PA (acceptance PAA01 ; CLAUDE.md n°6/14/16 ;
/// INV-TRANSMISSION-005) : aucun type HTTP ne traverse l'abstraction, le module ne référence AUCUNE
/// PA concrète (plug-in) ni un autre module métier. Ces gardes échouent dès qu'une dépendance
/// interdite est introduite — c'est leur raison d'être (elles sont triviales aujourd'hui car aucun
/// plug-in n'existe encore, mais elles verrouillent la frontière pour les items suivants).
/// </summary>
public sealed class TransmissionBoundaryTests
{
    private static readonly Assembly ContractsAssembly = typeof(IPaClient).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(PaClientRegistry).Assembly;

    [Fact]
    public void Abstraction_HasNoHttpType()
    {
        // Aucun type HTTP dans l'abstraction (F05 §6 : la construction du payload PA-spécifique est
        // interne au plug-in). NetArchTest échouerait si un type exposait HttpClient/HttpRequestMessage…
        var result = Types.InAssembly(ContractsAssembly)
            .Should()
            .NotHaveDependencyOnAny("System.Net.Http", "System.Net.Sockets", "System.Net.HttpListener")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "l'abstraction PA ne doit exposer aucun type HTTP — {0}",
            DescribeFailures(result));
    }

    [Fact]
    public void Contracts_DoesNotReferenceAnyConcretePaPlugin()
    {
        ReferencedAssemblyNames(ContractsAssembly)
            .Should().NotContain(name => name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "le module Transmission ne référence JAMAIS un plug-in PA concret (CLAUDE.md n°6/16)");
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceAnyConcretePaPlugin()
    {
        ReferencedAssemblyNames(InfrastructureAssembly)
            .Should().NotContain(name => name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "le registre résout par clé, sans connaître aucune PA concrète (CLAUDE.md n°16)");
    }

    [Fact]
    public void Contracts_DoesNotReachIntoAnotherBusinessModule()
    {
        // Seule dépendance inter-projet autorisée : le contrat agent partagé (PivotDocumentDto). Aucune
        // référence à un AUTRE module métier (module-rules §3, CLAUDE.md n°14).
        ReferencedAssemblyNames(ContractsAssembly)
            .Where(name => name.StartsWith("Liakont.Modules.", StringComparison.Ordinal))
            .Should().OnlyContain(name => name.StartsWith("Liakont.Modules.Transmission", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames ?? []);
}
