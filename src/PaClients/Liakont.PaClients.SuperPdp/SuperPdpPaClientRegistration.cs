namespace Liakont.PaClients.SuperPdp;

using System.Net.Security;
using System.Security.Authentication;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du plug-in Super PDP — même patron que <c>AddB2BrouterPaClient</c> (PAB) /
/// <c>AddFakePaClient</c> (PAA02) : ajoute UNIQUEMENT sa <see cref="SuperPdpClientFactory"/> à l'ensemble
/// des <see cref="IPaClientFactory"/>, que le registre du module Transmission découvre par clé (aucun
/// câblage produit spécifique à « SuperPdp » — CLAUDE.md n°6/8/16). Configure aussi le client HTTP nommé
/// du plug-in avec TLS 1.2/1.3 forcé (F14 §7).
/// </summary>
public static class SuperPdpPaClientRegistration
{
    /// <summary>
    /// Enregistre le plug-in Super PDP : client HTTP nommé (TLS 1.2/1.3) + fabrique en singleton.
    /// <para>
    /// PRÉREQUIS — le Host DOIT enregistrer un <see cref="ISuperPdpAccountResolver"/> (qui déchiffre les
    /// secrets OAuth du tenant via le coffre TenantSettings, hors de portée du plug-in) : la fabrique en
    /// dépend. Cette frontière garantit qu'aucun secret en clair ne transite par le descripteur de compte
    /// (CLAUDE.md n°10). Le câblage de cet adaptateur côté Host est livré avec l'assemblage du Host.
    /// </para>
    /// <para>
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// déduplique par type d'implémentation : un double appel n'enregistre pas deux fabriques Super PDP.
    /// </para>
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    public static IServiceCollection AddSuperPdpPaClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Client HTTP nommé : TLS 1.2/1.3 explicitement (F14 §7 — cohérent avec B2Brouter F05 §5).
        services.AddHttpClient(SuperPdpDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                },
            });

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPaClientFactory, SuperPdpClientFactory>());

        return services;
    }
}
