namespace Liakont.Modules.Signature.Infrastructure;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Validation au DÉMARRAGE des fournisseurs de signature CONFIGURÉS (ADR-0027 §4 ; INV-SIGPROV-6). C'est
/// l'équivalent de <c>IIdentityProviderAuthenticator.ValidateConfiguration()</c> côté composition root,
/// MAIS avec la différence essentielle que la signature est OPTIONNELLE :
/// <list type="bullet">
///   <item>l'ABSENCE de tout fournisseur configuré n'est JAMAIS une erreur — un tenant en
///   <see cref="SignatureLevel.Recorded"/> démarre sans aucun plug-in (la capacité reste indisponible) ;</item>
///   <item>on bloque le démarrage UNIQUEMENT pour un fournisseur effectivement CONFIGURÉ (déclaré dans
///   <c>Signature:EnabledProviders</c>) mais MALFORMÉ — ici : aucun plug-in ne l'implémente.</item>
/// </list>
/// Pur (testable sans le Host) : prend la liste configurée + le registre, ne touche à rien d'autre.
/// Bloquer la plateforme entière faute de signature configurée serait un durcissement non justifié
/// (CLAUDE.md n°3).
/// </summary>
public static class SignatureProviderStartupValidator
{
    /// <summary>
    /// Valide que chaque fournisseur configuré est bien câblé. No-op si <paramref name="configuredProviderTypes"/>
    /// est vide ou nul (signature optionnelle). Lève une <see cref="InvalidOperationException"/> avec un
    /// message opérateur français si un type configuré n'est enregistré par aucun plug-in.
    /// </summary>
    /// <param name="configuredProviderTypes">Types de fournisseurs déclarés activés (config <c>Signature:EnabledProviders</c>).</param>
    /// <param name="registry">Registre des plug-ins de signature effectivement câblés.</param>
    public static void Validate(
        IReadOnlyCollection<string>? configuredProviderTypes,
        ISignatureProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (configuredProviderTypes is null || configuredProviderTypes.Count == 0)
        {
            // Signature optionnelle : aucun fournisseur configuré n'est un état valide (défaut Recorded).
            return;
        }

        foreach (var providerType in configuredProviderTypes)
        {
            if (string.IsNullOrWhiteSpace(providerType))
            {
                throw new InvalidOperationException(
                    "Un fournisseur de signature configuré (Signature:EnabledProviders) a un type vide. "
                    + "Retirez l'entrée vide ou indiquez un type de plug-in valide.");
            }

            if (!registry.IsRegistered(providerType))
            {
                var disponibles = registry.RegisteredTypes.Count == 0
                    ? "aucun"
                    : string.Join(", ", registry.RegisteredTypes);
                throw new InvalidOperationException(
                    $"Le fournisseur de signature « {providerType} » est configuré "
                    + "(Signature:EnabledProviders) mais aucun plug-in ne l'implémente. "
                    + $"Plug-ins disponibles : {disponibles}. Câblez le plug-in correspondant, "
                    + "ou retirez ce fournisseur de la configuration (la signature est optionnelle).");
            }
        }
    }
}
