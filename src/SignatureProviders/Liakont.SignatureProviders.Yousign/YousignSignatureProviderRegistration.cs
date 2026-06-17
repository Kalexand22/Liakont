namespace Liakont.SignatureProviders.Yousign;

using System.Net.Http;
using Liakont.Modules.Signature.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du plug-in Yousign (ADR-0029), câblé au COMPOSITION ROOT — seul endroit autorisé à
/// référencer un plug-in concret (CLAUDE.md n°6/14). Enregistre le client HTTP nommé (anti-SSRF :
/// <see cref="YousignSsrfGuardHandler"/> + <c>AllowAutoRedirect = false</c>) et la
/// <see cref="YousignSignatureProviderFactory"/> en singleton ; le registre du module Signature la découvre
/// par son type. L'<see cref="IYousignAccountResolver"/> (déchiffrement via le coffre) est fourni par le Host
/// (il voit <c>ISecretProtector</c>, hors de portée du plug-in).
/// </summary>
public static class YousignSignatureProviderRegistration
{
    /// <summary>Ajoute le plug-in Yousign : client HTTP anti-SSRF + fabrique de provider (singleton).</summary>
    /// <param name="services">Collection de services de l'application.</param>
    public static IServiceCollection AddYousignSignatureProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Client HTTP nommé : anti-SSRF en DEUX couches (ADR-0029 §6 ; INV-YOUSIGN-7).
        // 1. Handler primaire AllowAutoRedirect=false → une redirection (3xx) n'est JAMAIS suivie automatiquement.
        // 2. YousignSsrfGuardHandler → refuse toute URI hors allowlist AVANT l'appel authentifié (Bearer).
        services.AddTransient<YousignSsrfGuardHandler>();
        services.AddHttpClient(YousignDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            })
            .AddHttpMessageHandler<YousignSsrfGuardHandler>();

        // Fabrique découverte par le registre du module Signature par ProviderType (CLAUDE.md n°6/16).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISignatureProviderFactory, YousignSignatureProviderFactory>());

        return services;
    }
}
