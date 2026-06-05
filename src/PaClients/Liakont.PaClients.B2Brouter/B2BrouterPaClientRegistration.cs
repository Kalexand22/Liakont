namespace Liakont.PaClients.B2Brouter;

using System.Net.Security;
using System.Security.Authentication;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du plug-in B2Brouter — même patron que <c>AddFakePaClient</c> (PAA02) : ajoute
/// UNIQUEMENT sa <see cref="B2BrouterClientFactory"/> à l'ensemble des <see cref="IPaClientFactory"/>,
/// que le registre du module Transmission découvre par clé (aucun câblage produit spécifique à
/// « B2Brouter » — CLAUDE.md n°6/8/16). Configure aussi le client HTTP nommé du plug-in avec TLS
/// 1.2/1.3 forcé (F05 §5).
/// </summary>
public static class B2BrouterPaClientRegistration
{
    /// <summary>
    /// Enregistre le plug-in B2Brouter : client HTTP nommé (TLS 1.2/1.3) + fabrique en singleton.
    /// <para>
    /// PRÉREQUIS — le Host DOIT enregistrer un <see cref="IB2BrouterAccountResolver"/> (qui déchiffre
    /// la clé API du tenant via le coffre TenantSettings, hors de portée du plug-in) : la fabrique en
    /// dépend. Cette frontière garantit qu'aucun secret en clair ne transite par le descripteur de
    /// compte (CLAUDE.md n°10). Le câblage de cet adaptateur côté Host est livré avec l'assemblage du
    /// Host (pipeline PIP), comme pour les autres plug-ins.
    /// </para>
    /// <para>
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// déduplique par type d'implémentation : un double appel n'enregistre pas deux fabriques B2Brouter.
    /// </para>
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    public static IServiceCollection AddB2BrouterPaClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Client HTTP nommé : TLS 1.2/1.3 explicitement (F05 §5 — certains Windows Server n'activent pas
        // TLS 1.2 par défaut). Le retry/backoff (resilience) est ajouté par PAB02 sur ce même client.
        services.AddHttpClient(B2BrouterDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                },
            });

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPaClientFactory, B2BrouterClientFactory>());

        return services;
    }
}
