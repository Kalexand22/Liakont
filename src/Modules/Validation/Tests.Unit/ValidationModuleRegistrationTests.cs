namespace Liakont.Modules.Validation.Tests.Unit;

using System.Linq;
using FluentAssertions;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Garde anti faux-vert : <c>AddValidationModule</c> branche <see cref="IValidationService"/> ET un ensemble
/// NON VIDE de règles (<see cref="IDocumentRule"/>). Un validateur de conformité qui passerait en silence
/// faute de règles violerait « bloquer plutôt qu'envoyer faux » (CLAUDE.md n°3) : le pipeline (PIP01b) résout
/// ce service en croyant valider, alors qu'il déclarerait tout document conforme.
/// </summary>
public sealed class ValidationModuleRegistrationTests
{
    [Fact]
    public void AddValidationModule_Registers_The_Validation_Service()
    {
        var services = new ServiceCollection();

        services.AddValidationModule();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IValidationService) &&
            d.ImplementationType == typeof(ValidationService));
    }

    [Fact]
    public void AddValidationModule_Registers_A_Non_Empty_Set_Of_Rules()
    {
        var services = new ServiceCollection();

        services.AddValidationModule();

        services.Count(d => d.ServiceType == typeof(IDocumentRule)).Should().BeGreaterThanOrEqualTo(
            12, "un validateur ne doit jamais passer en silence faute de règles (CLAUDE.md n°3)");
    }
}
