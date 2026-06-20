namespace Liakont.Host.PaDelivery;

using Liakont.PaClients.ChorusPro;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Câblage au COMPOSITION ROOT du plug-in PA Chorus Pro (F18) — le seul endroit autorisé à référencer un
/// plug-in PA concret (CLAUDE.md n°6/14). Enregistre le résolveur de compte Host (qui déchiffre les secrets
/// PISTE + compte technique par tenant via le coffre TenantSettings) AVANT la fabrique, qui en dépend
/// (patron <see cref="GeneriquePaDeliveryBootstrap"/> / câblage Super PDP). Le plug-in lui-même ne référence
/// que <c>Transmission.Contracts</c> : il ne voit ni le coffre ni TenantSettings.
/// </summary>
public static class ChorusProPaDeliveryBootstrap
{
    /// <summary>
    /// Enregistre le résolveur de compte Chorus Pro (secrets déchiffrés via <c>ISecretProtector</c> +
    /// lecture du coffre <c>pa_accounts</c>) et la fabrique du plug-in Chorus Pro. Le registre du module
    /// Transmission découvre la fabrique par <c>PaType « ChorusPro »</c> (aucun <c>if (pa is …)</c>).
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    public static IServiceCollection AddChorusProPaDelivery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Frontière coffre du tenant : déchiffre les secrets PISTE + compte technique par tenant (jamais en
        // clair dans le descripteur). Enregistré AVANT la fabrique (AddChorusProPaClient en dépend).
        services.TryAddSingleton<IChorusProAccountResolver, ChorusProAccountResolver>();

        // Fabrique du plug-in (résolue par PaType « ChorusPro » par le registre — aucun if (pa is …)).
        services.AddChorusProPaClient();

        return services;
    }
}
