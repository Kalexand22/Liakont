namespace Liakont.Host.PaDelivery;

using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Generique;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Câblage au COMPOSITION ROOT du plug-in PA GÉNÉRIQUE (F16 §6) et de ses canaux de livraison Host
/// (email avec pièce jointe, dépôt de fichier) — le seul endroit autorisé à référencer un plug-in PA
/// concret (CLAUDE.md n°6/14). Les canaux et le résolveur (qui déchiffre le secret SMTP par tenant via le
/// coffre) sont des implémentations Host-only de contrats définis dans <c>Transmission.Contracts</c> : le
/// plug-in ne référence ni MailKit ni le module Notification.
/// <para>
/// ⚠️ NON appelé tant que FX07 n'a pas livré l'intégration pipeline : enregistrer la fabrique sans le flux
/// qui GÉNÈRE et passe le Factur-X (extension du contrat IPaClient + génération à l'étape Sending, FX07)
/// rendrait un compte « Generique » actif non transmissible (SendDocumentAsync bloquerait à chaque envoi).
/// FX07 appelle <see cref="AddGeneriquePaDelivery"/> au composition root EN MÊME TEMPS qu'il câble la
/// génération — le plug-in n'est résolu par le registre qu'à partir de ce moment.
/// </para>
/// </summary>
public static class GeneriquePaDeliveryBootstrap
{
    /// <summary>
    /// Enregistre les canaux de livraison Host (email / dépôt de fichier), le résolveur de compte
    /// générique (secrets déchiffrés via <c>ISecretProtector</c>) et la fabrique du plug-in générique.
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    public static IServiceCollection AddGeneriquePaDelivery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Canaux de livraison (Host-only) — découverts par le plug-in via IEnumerable<IDocumentDeliveryChannel>.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDocumentDeliveryChannel, EmailDocumentDeliveryChannel>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDocumentDeliveryChannel, FileDepositDocumentDeliveryChannel>());

        // Frontière coffre du tenant : déchiffre le secret SMTP par tenant (jamais en clair dans le descripteur).
        services.TryAddSingleton<IGeneriqueAccountResolver, GeneriqueAccountResolver>();

        // Fabrique du plug-in (résolue par PaType « Generique » par le registre — aucun if (pa is …)).
        services.AddGeneriquePaClient();

        return services;
    }
}
