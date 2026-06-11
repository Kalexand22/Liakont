namespace Liakont.PaClients.SuperPdp;

/// <summary>
/// Configuration RÉSOLUE d'un compte Super PDP, prête à l'emploi par le client : un compte =
/// (environnement, identifiant de compte, <c>client_id</c> + <c>client_secret</c> OAuth) — F14 §3.1/§7
/// (« un SuperPdpClient = un compte »). Produite par un <see cref="ISuperPdpAccountResolver"/>
/// (implémenté par le Host, qui déchiffre les secrets via le coffre du tenant).
/// <para>
/// ⚠️ Porte le <see cref="ClientSecret"/> EN CLAIR : objet de transport EN MÉMOIRE uniquement. Il ne
/// doit JAMAIS être persisté ni journalisé (CLAUDE.md n°10) — <see cref="ToString"/> est donc redéfini
/// pour CAVIARDER le secret (et l'identifiant client).
/// </para>
/// </summary>
public sealed record SuperPdpAccountConfig
{
    /// <summary>Crée une configuration de compte Super PDP résolue.</summary>
    /// <param name="environment">Environnement du compte (détermine l'URL de base — F14 §3.1).</param>
    /// <param name="accountId">Identifiant de compte Super PDP (audit + lectures). Obligatoire.</param>
    /// <param name="clientId">Identifiant client OAuth 2.0 (déchiffré par le Host). Obligatoire.</param>
    /// <param name="clientSecret">Secret client OAuth 2.0, EN CLAIR (déchiffré par le Host). Obligatoire.</param>
    public SuperPdpAccountConfig(
        SuperPdpEnvironment environment,
        string accountId,
        string clientId,
        string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("L'identifiant de compte Super PDP est obligatoire.", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Le client_id OAuth Super PDP est obligatoire.", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Le client_secret OAuth Super PDP est obligatoire.", nameof(clientSecret));
        }

        Environment = environment;
        AccountId = accountId;
        ClientId = clientId;
        ClientSecret = clientSecret;
    }

    /// <summary>Environnement du compte (sandbox / production).</summary>
    public SuperPdpEnvironment Environment { get; }

    /// <summary>Identifiant de compte Super PDP.</summary>
    public string AccountId { get; }

    /// <summary>Identifiant client OAuth 2.0 (transport mémoire — ne jamais persister/journaliser).</summary>
    public string ClientId { get; }

    /// <summary>Secret client OAuth 2.0, EN CLAIR (transport mémoire — ne jamais persister/journaliser).</summary>
    public string ClientSecret { get; }

    /// <summary>
    /// URL de base de l'API selon l'environnement (F14 §3.1). L'hôte sandbox est confirmé (test OAuth réel
    /// du 2026-06-11). La base PRODUCTION n'est PAS confirmée (F14 §12 O1) : accéder à cette propriété pour
    /// un compte <c>Production</c> lève <see cref="NotSupportedException"/> — bloquer plutôt qu'envoyer faux
    /// (CLAUDE.md n°3). La structure de sélection par environnement est conservée : le jour où PAS03 confirme
    /// la base de production, il suffit d'ajouter le bras <c>Production</c> ici (et dans
    /// <see cref="SuperPdpDefaults"/>) sans autre refactorisation.
    /// </summary>
    public Uri BaseUrl => Environment switch
    {
        SuperPdpEnvironment.Sandbox => new Uri(SuperPdpDefaults.SandboxBaseUrl, UriKind.Absolute),
        _ => throw new NotSupportedException(
            $"La base d'API Super PDP PRODUCTION n'est pas confirmée (F14 §12 O1). " +
            $"Un compte configuré en environnement « {Environment} » ne peut pas être utilisé avant que PAS03 " +
            $"établisse l'URL de production. Configurez le compte en environnement Sandbox jusqu'à cette confirmation."),
    };

    /// <summary>Représentation CAVIARDÉE : ne révèle jamais les secrets OAuth (CLAUDE.md n°10).</summary>
    public override string ToString() =>
        $"SuperPdpAccountConfig {{ Environment = {Environment}, AccountId = {AccountId}, ClientId = ***, ClientSecret = *** }}";
}
