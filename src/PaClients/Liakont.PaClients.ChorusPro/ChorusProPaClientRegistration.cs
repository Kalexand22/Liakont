namespace Liakont.PaClients.ChorusPro;

using System.Net.Security;
using System.Security.Authentication;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du plug-in Chorus Pro — même patron que <c>AddSuperPdpPaClient</c> (PAS) /
/// <c>AddB2BrouterPaClient</c> (PAB) : ajoute UNIQUEMENT sa <see cref="ChorusProClientFactory"/> à
/// l'ensemble des <see cref="IPaClientFactory"/>, que le registre du module Transmission découvre par clé
/// (aucun câblage produit spécifique à « ChorusPro » — CLAUDE.md n°6/8/16). Configure aussi le client HTTP
/// nommé du plug-in avec TLS 1.2/1.3 forcé (F18 §7), consommé par le transport HTTP livré à partir de CP03.
/// </summary>
public static class ChorusProPaClientRegistration
{
    /// <summary>
    /// Enregistre le plug-in Chorus Pro : client HTTP nommé (TLS 1.2/1.3) + fabrique en singleton.
    /// <para>
    /// PRÉREQUIS — le Host DOIT enregistrer un <see cref="IChorusProAccountResolver"/> (qui déchiffre les
    /// secrets PISTE + compte technique du tenant via le coffre TenantSettings, hors de portée du plug-in) :
    /// la fabrique en dépend. Cette frontière garantit qu'aucun secret en clair ne transite par le
    /// descripteur de compte (CLAUDE.md n°10). Le câblage de cet adaptateur côté Host est livré avec
    /// l'assemblage du Host.
    /// </para>
    /// <para>
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// déduplique par type d'implémentation : un double appel n'enregistre pas deux fabriques Chorus Pro.
    /// </para>
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    public static IServiceCollection AddChorusProPaClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Client HTTP nommé : TLS 1.2/1.3 explicitement (F18 §7 — cohérent avec Super PDP F14 §7).
        // Consommé par le transport HTTP du plug-in à partir de CP03 (dépôt deposerFluxFacture / OAuth2).
        services.AddHttpClient(ChorusProDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                },
            });

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPaClientFactory, ChorusProClientFactory>());

        return services;
    }
}
