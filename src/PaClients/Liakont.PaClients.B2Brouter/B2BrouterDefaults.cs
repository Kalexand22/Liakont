namespace Liakont.PaClients.B2Brouter;

/// <summary>
/// Constantes du plug-in B2Brouter — toutes des FAITS validés en staging (F05 §2, « ne pas
/// re-découvrir »), jamais des valeurs inventées (CLAUDE.md n°2/15). Centralisées ici pour qu'un
/// changement d'API (nouvelle version, nouvelle URL) tienne en un seul point.
/// </summary>
public static class B2BrouterDefaults
{
    /// <summary>Clé de registre du plug-in (résolution par <see cref="Modules.Transmission.Contracts.PaAccountDescriptor.PaType"/>, insensible à la casse).</summary>
    public const string PaTypeKey = "B2Brouter";

    /// <summary>Nom affichable de la PA, porté dans les messages opérateur français (CLAUDE.md n°12).</summary>
    public const string PaName = "B2Brouter";

    /// <summary>Nom du client HTTP nommé enregistré via <c>AddHttpClient</c> (F05 §5).</summary>
    public const string HttpClientName = "Liakont.PaClients.B2Brouter";

    /// <summary>En-tête d'authentification — clé statique du compte PA (F05 §2, RECAP B.1).</summary>
    public const string ApiKeyHeader = "X-B2B-API-Key";

    /// <summary>En-tête de version d'API (F05 §2, RECAP B.1 / Doc API).</summary>
    public const string ApiVersionHeader = "X-B2B-API-Version";

    /// <summary>Version d'API minimale exigée pour les champs DGFiP (F05 §2).</summary>
    public const string MinApiVersion = "2026-03-02";

    /// <summary>
    /// URL de base STAGING — bien <c>api-staging</c> et PAS <c>app-staging</c>, qui provoque
    /// <c>api_version_subdomain_mismatch</c> (F05 §2, RECAP B.1).
    /// </summary>
    public const string StagingBaseUrl = "https://api-staging.b2brouter.net";

    /// <summary>URL de base PRODUCTION (F05 §2, Doc API).</summary>
    public const string ProductionBaseUrl = "https://api.b2brouter.net";

    /// <summary>
    /// Délai d'attente HTTP par appel : 60 s (l'API peut être lente à la création + envoi — F05 §4.3).
    /// </summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(60);
}
