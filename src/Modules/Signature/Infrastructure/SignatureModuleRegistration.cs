namespace Liakont.Modules.Signature.Infrastructure;

using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Signature (ADR-0027 + ADR-0030) : le registre de types des plug-ins de
/// signature, le plug-in SUR PLACE Wacom (SIG08), et la persistance du proxy <c>OnSiteCapture</c> (liaisons de
/// signataire vérifié + journal de preuve append-only + migrations DbUp du schéma <c>signature</c>). Les
/// plug-ins ajoutent leur <see cref="ISignatureProviderFactory"/> en singleton ; le registre les découvre
/// automatiquement — le Host n'a rien d'autre à câbler par fournisseur (CLAUDE.md n°6/8/16). La signature est
/// OPTIONNELLE : un ensemble vide de plug-ins est valide ; la validation au démarrage ne bloque QUE pour un
/// fournisseur configuré mais non câblé (<see cref="SignatureProviderStartupValidator"/>).
/// </summary>
public static class SignatureModuleRegistration
{
    /// <summary>
    /// Enregistre le registre de plug-ins de signature, le plug-in Wacom sur place, le proxy OnSiteCapture
    /// (handlers MediatR + stores tenant-scopés) et les migrations du module.
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddSignatureModule(this IServiceCollection services)
    {
        services.TryAddSingleton<ISignatureProviderRegistry, SignatureProviderRegistry>();

        // Plug-in SUR PLACE (Wacom, ADR-0030) : déclaré comme fabrique singleton, découverte par le registre
        // (jamais un if (type == "Wacom")). Capacités fixes {SES}, biometric template matching=false.
        services.AddSingleton<ISignatureProviderFactory, OnSiteSignatureProviderFactory>();

        // Migrations DbUp tenant-scopées du schéma signature (créées dans CHAQUE base tenant au provisioning).
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(SignatureModuleRegistration).Assembly));

        // Proxy OnSiteCapture (SIG08) : handlers MediatR (capture + enregistrement du signataire vérifié) et
        // stores Dapper tenant-scopés (journal de preuve append-only + liaisons de signataire vérifié).
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SignatureModuleRegistration).Assembly));
        services.AddScoped<IOnSiteSignatureProofStore, PostgresOnSiteSignatureProofStore>();
        services.AddScoped<IOnSiteSignerBindingStore, PostgresOnSiteSignerBindingStore>();

        return services;
    }
}
