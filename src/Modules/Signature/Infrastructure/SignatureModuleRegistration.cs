namespace Liakont.Modules.Signature.Infrastructure;

using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure.Drain;
using Liakont.Modules.Signature.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Signature (ADR-0027/0029) : le registre de types des plug-ins de signature, la
/// persistance tenant-scopée (comptes chiffrés, inbox durable, liaison demande→document — schéma <c>signature</c>),
/// le catalogue SYSTÈME de routage par handle opaque, et le drain WORM. Les plug-ins (Yousign = SIG07,
/// Wacom = SIG08) ajoutent leur <see cref="ISignatureProviderFactory"/> en singleton au composition root ; le
/// registre les découvre automatiquement (CLAUDE.md n°6/8/16). La signature est OPTIONNELLE : aucun plug-in
/// enregistré est un état valide (le registre se construit vide), la validation au démarrage ne bloque QUE
/// pour un fournisseur configuré mais non câblé (<see cref="SignatureProviderStartupValidator"/>).
/// </summary>
public static class SignatureModuleRegistration
{
    /// <summary>
    /// Enregistre le registre de plug-ins de signature, la persistance (migrations du schéma <c>signature</c> +
    /// stores tenant-scopés + catalogue de routes système) et le service de drain WORM. Le registre capture
    /// l'ensemble des <see cref="ISignatureProviderFactory"/> du conteneur (fabriques en singleton).
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddSignatureModule(this IServiceCollection services)
    {
        services.TryAddSingleton<ISignatureProviderRegistry, SignatureProviderRegistry>();

        // Migrations du schéma signature (tenant-scopées + catalogue de routes système) scannées par DbUp.
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(SignatureModuleRegistration).Assembly));

        // Persistance tenant-scopée (IConnectionFactory scopé) + catalogue de routes SYSTÈME (ISystemConnectionFactory).
        services.TryAddScoped<ISignatureAccountStore, PostgresSignatureAccountStore>();
        services.TryAddScoped<ISignatureWebhookInbox, PostgresSignatureWebhookInbox>();
        services.TryAddScoped<ISignatureRequestStore, PostgresSignatureRequestStore>();
        services.TryAddSingleton<ISignatureWebhookRouteCatalog, PostgresSignatureWebhookRouteCatalog>();

        // Drain WORM (scopé tenant, résolu par SignatureWebhookDrainJob via TenantJobRunner).
        services.TryAddScoped<ISignatureWebhookDrainService, SignatureWebhookDrainService>();

        return services;
    }
}
