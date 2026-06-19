namespace Liakont.PaClients.Generique;

using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du plug-in générique — même patron que <c>AddSuperPdpPaClient</c> (PAS) /
/// <c>AddFakePaClient</c> (PAA02) : ajoute UNIQUEMENT sa <see cref="GeneriqueClientFactory"/> à l'ensemble
/// des <see cref="IPaClientFactory"/>, que le registre du module Transmission découvre par clé (aucun
/// câblage produit spécifique à « Generique » — CLAUDE.md n°6/8/16).
/// <para>
/// PRÉREQUIS — le Host DOIT enregistrer un <see cref="IGeneriqueAccountResolver"/> (qui déchiffre le secret
/// SMTP éventuel du tenant via le coffre, hors de portée du plug-in) ET au moins une implémentation de
/// <see cref="IDocumentDeliveryChannel"/> (email / dépôt de fichier, Host-only) : la fabrique en dépend.
/// Cette frontière garantit qu'aucun secret en clair ne transite par le descripteur (CLAUDE.md n°10) et
/// que le plug-in ne référence ni MailKit ni le module Notification (F16 §6.2).
/// </para>
/// </summary>
public static class GeneriquePaClientRegistration
{
    /// <summary>Enregistre la fabrique du plug-in générique en singleton (déduplication par type d'implémentation).</summary>
    /// <param name="services">Collection de services de l'application.</param>
    public static IServiceCollection AddGeneriquePaClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPaClientFactory, GeneriqueClientFactory>());

        return services;
    }
}
