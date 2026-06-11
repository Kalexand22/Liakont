namespace Liakont.Host.Startup;

using Microsoft.AspNetCore.HttpOverrides;

/// <summary>
/// Construit, de façon TESTABLE, les options <c>ForwardedHeaders</c> de l'appliance (reverse proxy
/// Caddy, F12 §6.2/6.6) à partir de la section de configuration <c>"ForwardedHeaders"</c>.
/// Désactivé par défaut → un accès direct (dev/test, sans proxy) reste inchangé.
/// </summary>
internal static class ForwardedHeadersConfiguration
{
    /// <summary>
    /// Retourne les options à appliquer, ou <c>null</c> si la prise en charge du reverse proxy est
    /// désactivée (<c>ForwardedHeaders:Enabled</c> absent ou <c>false</c>). La confiance loopback par
    /// défaut est TOUJOURS vidée : seuls les réseaux/proxys explicitement déclarés sont de confiance
    /// (sinon X-Forwarded-For serait usurpable et la limite par IP contournable). Un CIDR/une IP
    /// invalide lève au démarrage — échec VISIBLE, jamais une confiance silencieusement mal posée.
    /// </summary>
    public static ForwardedHeadersOptions? Build(IConfigurationSection section)
    {
        if (!section.GetValue<bool>("Enabled"))
        {
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost,
        };

        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var cidr in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
        }

        foreach (var proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
        }

        return options;
    }
}
