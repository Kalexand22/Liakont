namespace Liakont.Modules.Signature.Infrastructure;

using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure.Drain;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Liakont.Modules.Signature.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Signature (ADR-0027 + ADR-0029 + ADR-0030) : le registre de types des plug-ins,
/// le plug-in SUR PLACE Wacom intégré (SIG08), la persistance tenant-scopée du distant Yousign (comptes
/// chiffrés, inbox durable, liaison demande→document, catalogue SYSTÈME de routage par handle opaque, drain
/// WORM — SIG07), et la persistance du proxy <c>OnSiteCapture</c> (liaisons de signataire vérifié + journal de
/// preuve append-only — SIG08), le tout sur le schéma <c>signature</c> (migrations DbUp). Les plug-ins ajoutent
/// leur <see cref="ISignatureProviderFactory"/> en singleton ; le registre les découvre automatiquement (jamais
/// un <c>if (type == …)</c> — CLAUDE.md n°6/8/16). La signature est OPTIONNELLE : un ensemble vide de plug-ins
/// est valide ; la validation au démarrage ne bloque QUE pour un fournisseur configuré mais non câblé
/// (<see cref="SignatureProviderStartupValidator"/>).
/// </summary>
public static class SignatureModuleRegistration
{
    /// <summary>
    /// Enregistre le registre de plug-ins, le plug-in Wacom sur place, la persistance Yousign (stores +
    /// catalogue de routes + drain WORM), le proxy OnSiteCapture (handlers MediatR + stores tenant-scopés) et
    /// les migrations du schéma <c>signature</c>.
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddSignatureModule(this IServiceCollection services)
    {
        services.TryAddSingleton<ISignatureProviderRegistry, SignatureProviderRegistry>();

        // Plug-in SUR PLACE (Wacom, ADR-0030) : fabrique singleton découverte par le registre (jamais un
        // if (type == "Wacom")). Capacités fixes {SES}, biometric template matching=false (INV-ONSITE-8/10).
        services.AddSingleton<ISignatureProviderFactory, OnSiteSignatureProviderFactory>();

        // Migrations DbUp tenant-scopées du schéma signature (Yousign SIG07 + OnSite SIG08, même assembly,
        // créées dans CHAQUE base tenant au provisioning) — enregistrées une seule fois.
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(SignatureModuleRegistration).Assembly));

        // Persistance du distant Yousign (SIG07, ADR-0029) : stores tenant-scopés + catalogue de routes SYSTÈME
        // + drain WORM (scopé tenant, résolu par SignatureWebhookDrainJob via TenantJobRunner).
        services.TryAddScoped<ISignatureAccountStore, PostgresSignatureAccountStore>();
        services.TryAddScoped<ISignatureWebhookInbox, PostgresSignatureWebhookInbox>();
        services.TryAddScoped<ISignatureRequestStore, PostgresSignatureRequestStore>();
        services.TryAddSingleton<ISignatureWebhookRouteCatalog, PostgresSignatureWebhookRouteCatalog>();
        services.TryAddScoped<ISignatureWebhookDrainService, SignatureWebhookDrainService>();

        // Proxy OnSiteCapture (SIG08, ADR-0030) : handlers MediatR (capture + enregistrement du signataire
        // vérifié) et stores Dapper tenant-scopés (journal de preuve append-only + liaisons de signataire vérifié).
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SignatureModuleRegistration).Assembly));
        services.AddScoped<IOnSiteSignatureProofStore, PostgresOnSiteSignatureProofStore>();
        services.AddScoped<IOnSiteSignerBindingStore, PostgresOnSiteSignerBindingStore>();

        return services;
    }
}
