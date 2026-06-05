namespace Liakont.Modules.Validation.Infrastructure;

using Liakont.Modules.Validation.Contracts;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Enregistrement DI du module Validation (F04) : expose <see cref="IValidationService"/> à la frontière
/// Contracts, wrappant le <c>ValidationPipeline</c> du domaine sur l'ensemble des règles
/// (<see cref="IDocumentRule"/>) enregistrées par les items VAL02-VAL05. AUCUNE règle n'est enregistrée
/// ici (chaque règle arrive avec son item) ; sans règle, la validation passe (aucune anomalie).
/// </summary>
public static class ValidationModuleRegistration
{
    /// <summary>Enregistre le service de validation (frontière Contracts du module Validation).</summary>
    /// <param name="services">La collection de services.</param>
    /// <returns>La collection de services, pour chaînage.</returns>
    public static IServiceCollection AddValidationModule(this IServiceCollection services)
    {
        services.AddScoped<IValidationService, ValidationService>();
        return services;
    }
}
