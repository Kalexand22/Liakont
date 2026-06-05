namespace Liakont.Agent.Core.Transport;

using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Résultat d'une lecture de configuration (GET /api/agent/v1/configuration — F12 §3.2), interrogée
/// au démarrage du service. Quand <see cref="Kind"/> vaut <see cref="PlatformResponseKind.Ok"/> :
/// <see cref="Configuration"/> porte la configuration courante du tenant. Pour toute autre catégorie
/// (plateforme injoignable, clé invalide…), l'agent DÉMARRE quand même avec sa configuration locale
/// (F12 §2.5 — repli sur le fichier local + buffer).
/// </summary>
public sealed class ConfigurationOutcome
{
    /// <summary>Crée un résultat de lecture de configuration.</summary>
    /// <param name="kind">Catégorie de réponse de la plateforme.</param>
    /// <param name="configuration">Configuration courante renvoyée (renseignée uniquement pour une réponse 200).</param>
    /// <param name="reason">Détail / diagnostic, si applicable.</param>
    public ConfigurationOutcome(
        PlatformResponseKind kind,
        AgentConfigurationDto? configuration = null,
        string? reason = null)
    {
        Kind = kind;
        Configuration = configuration;
        Reason = reason;
    }

    /// <summary>Catégorie de réponse de la plateforme.</summary>
    public PlatformResponseKind Kind { get; }

    /// <summary>Configuration courante renvoyée (<c>null</c> hors réponse 200 exploitable).</summary>
    public AgentConfigurationDto? Configuration { get; }

    /// <summary>Détail / diagnostic.</summary>
    public string? Reason { get; }
}
