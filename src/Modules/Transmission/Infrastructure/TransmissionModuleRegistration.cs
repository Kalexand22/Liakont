namespace Liakont.Modules.Transmission.Infrastructure;

using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du module Transmission (PAA01) : le registre de types des plug-ins PA. Les
/// plug-ins (PAA02 Fake, PAB B2Brouter, PAS Super PDP) ajoutent ensuite leur
/// <see cref="IPaClientFactory"/> en singleton ; le registre les découvre automatiquement — le Host
/// n'a rien d'autre à câbler pour chaque nouvelle PA (CLAUDE.md n°6/8/16).
/// </summary>
public static class TransmissionModuleRegistration
{
    /// <summary>
    /// Enregistre le registre de plug-ins PA (<see cref="IPaClientRegistry"/>) en singleton. Le
    /// registre capture l'ensemble des <see cref="IPaClientFactory"/> du conteneur, d'où des
    /// fabriques attendues en singleton (composition pure, sans état par requête).
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    public static IServiceCollection AddTransmissionModule(this IServiceCollection services)
    {
        services.TryAddSingleton<IPaClientRegistry, PaClientRegistry>();
        return services;
    }
}
