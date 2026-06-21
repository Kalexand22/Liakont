namespace Liakont.Host.Security.Abstractions;

/// <summary>
/// Abstraction d'IdP (décision D10, 2026-06-03) : l'authentification OIDC et le
/// provisioning utilisateur/rôles sont consommés DERRIÈRE cette abstraction.
/// Le Host ne fait aucun appel spécifique à un IdP concret hors de la couche d'auth.
/// </summary>
/// <remarks>
/// Keycloak est UNE (et à ce jour la SEULE) implémentation
/// (<see cref="Keycloak.KeycloakIdentityProviderAuthenticator"/>) ; 0 implémentation OpenIddict.
/// Cette abstraction borne le couplage à un IdP concret à la couche d'auth ; brancher une
/// alternative in-process (par exemple OpenIddict) reste à faire et ne se réduit PAS à changer le
/// sélecteur d'<c>AppBootstrap</c> (provisioning realm/utilisateur, 2FA, résolution issuer/JWKS
/// sont Keycloak-spécifiques et câblés hors du sélecteur — voir avenant ADR-0002 du 2026-06-20 / RDF09).
/// </remarks>
internal interface IIdentityProviderAuthenticator
{
    /// <summary>
    /// Nom du fournisseur d'identité (par exemple « Keycloak »), à des fins de
    /// diagnostic et de sélection.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Valide la configuration du fournisseur d'identité. Lève une exception si la
    /// configuration est absente ou invalide, afin de bloquer le démarrage plutôt
    /// que de laisser l'application tourner sans authentification correcte.
    /// </summary>
    void ValidateConfiguration();

    /// <summary>
    /// Enregistre le pipeline d'authentification (schémas OIDC, JwtBearer, cookie…)
    /// sur le <paramref name="builder"/> fourni.
    /// </summary>
    /// <param name="builder">Le constructeur de l'application web hôte.</param>
    void ConfigureAuthentication(WebApplicationBuilder builder);
}
