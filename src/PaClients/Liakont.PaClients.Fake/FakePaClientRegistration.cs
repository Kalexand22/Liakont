namespace Liakont.PaClients.Fake;

using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du plug-in factice (PAA02) — c'est ainsi que le Host le branche pour le mode démo
/// hors-ligne et les tests. Il ajoute UNIQUEMENT sa <see cref="FakePaClientFactory"/> à l'ensemble des
/// <see cref="IPaClientFactory"/> ; le registre du module Transmission la découvre automatiquement
/// (aucun câblage produit spécifique à « Fake » — CLAUDE.md n°6/8/16). Même patron que ce que livreront
/// les plug-ins B2Brouter (PAB) et Super PDP (PAS).
/// </summary>
public static class FakePaClientRegistration
{
    /// <summary>
    /// Enregistre la fabrique du plug-in factice en singleton (capturée par le registre singleton).
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// déduplique par type d'implémentation : un double appel n'enregistre pas deux fabriques « Fake »
    /// (ce qui ferait lever le registre pour type dupliqué).
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <param name="options">Configuration du plug-in factice, ou <c>null</c> pour les valeurs par défaut.</param>
    public static IServiceCollection AddFakePaClient(
        this IServiceCollection services,
        FakePaClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPaClientFactory>(new FakePaClientFactory(options)));
        return services;
    }
}
