namespace Liakont.Modules.Signature.Infrastructure;

using Liakont.Modules.Signature.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du module Signature (ADR-0027) : le registre de types des plug-ins de signature. Les
/// plug-ins (Yousign = SIG07, Wacom = SIG08) ajoutent ensuite leur <see cref="ISignatureProviderFactory"/>
/// en singleton ; le registre les découvre automatiquement — le Host n'a rien d'autre à câbler pour chaque
/// nouveau fournisseur (CLAUDE.md n°6/8/16). La signature est OPTIONNELLE : aucun plug-in enregistré est un
/// état valide (le registre se construit vide), la validation au démarrage ne bloque QUE pour un fournisseur
/// configuré mais non câblé (<see cref="SignatureProviderStartupValidator"/>).
/// </summary>
public static class SignatureModuleRegistration
{
    /// <summary>
    /// Enregistre le registre de plug-ins de signature (<see cref="ISignatureProviderRegistry"/>) en
    /// singleton. Le registre capture l'ensemble des <see cref="ISignatureProviderFactory"/> du conteneur,
    /// d'où des fabriques attendues en singleton (composition pure, sans état par requête).
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddSignatureModule(this IServiceCollection services)
    {
        services.TryAddSingleton<ISignatureProviderRegistry, SignatureProviderRegistry>();
        return services;
    }
}
