namespace Liakont.PaClients.B2Brouter;

/// <summary>
/// Configuration RÉSOLUE d'un compte B2Brouter, prête à l'emploi par le client : un compte =
/// (URL de base, identifiant de compte, clé API, version d'API) — F05 §2/§4.4 (« un
/// B2BrouterClient = un compte »). Produite par un <see cref="IB2BrouterAccountResolver"/>
/// (implémenté par le Host, qui déchiffre la clé via le coffre du tenant).
/// <para>
/// ⚠️ Porte la clé API EN CLAIR (<see cref="ApiKey"/>) : objet de transport EN MÉMOIRE uniquement.
/// Il ne doit JAMAIS être persisté ni journalisé (CLAUDE.md n°10) — <see cref="ToString"/> est
/// donc redéfini pour CAVIARDER la clé.
/// </para>
/// </summary>
public sealed record B2BrouterAccountConfig
{
    /// <summary>Crée une configuration de compte B2Brouter résolue.</summary>
    /// <param name="environment">Environnement du compte (détermine l'URL de base — F05 §2).</param>
    /// <param name="accountId">Identifiant de compte B2Brouter (segment d'URL des endpoints). Obligatoire.</param>
    /// <param name="apiKey">Clé API statique du compte, EN CLAIR (déchiffrée par le Host). Obligatoire.</param>
    /// <param name="apiVersion">
    /// Version d'API à envoyer (en-tête <c>X-B2B-API-Version</c>) ; <c>null</c> = <see cref="B2BrouterDefaults.MinApiVersion"/>.
    /// </param>
    public B2BrouterAccountConfig(
        B2BrouterEnvironment environment,
        string accountId,
        string apiKey,
        string? apiVersion = null)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("L'identifiant de compte B2Brouter est obligatoire.", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("La clé API B2Brouter est obligatoire.", nameof(apiKey));
        }

        Environment = environment;
        AccountId = accountId;
        ApiKey = apiKey;
        ApiVersion = string.IsNullOrWhiteSpace(apiVersion) ? B2BrouterDefaults.MinApiVersion : apiVersion;
    }

    /// <summary>Environnement du compte (staging / production).</summary>
    public B2BrouterEnvironment Environment { get; }

    /// <summary>Identifiant de compte B2Brouter.</summary>
    public string AccountId { get; }

    /// <summary>Clé API statique du compte, EN CLAIR (transport mémoire — ne jamais persister/journaliser).</summary>
    public string ApiKey { get; }

    /// <summary>Version d'API envoyée dans l'en-tête <c>X-B2B-API-Version</c>.</summary>
    public string ApiVersion { get; }

    /// <summary>URL de base de l'API selon l'environnement (F05 §2).</summary>
    public Uri BaseUrl => new(
        Environment == B2BrouterEnvironment.Production
            ? B2BrouterDefaults.ProductionBaseUrl
            : B2BrouterDefaults.StagingBaseUrl,
        UriKind.Absolute);

    /// <summary>Représentation CAVIARDÉE : ne révèle jamais la clé API (CLAUDE.md n°10).</summary>
    public override string ToString() =>
        $"B2BrouterAccountConfig {{ Environment = {Environment}, AccountId = {AccountId}, ApiKey = ***, ApiVersion = {ApiVersion} }}";
}
