namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Mode d'authentification déclaré par un type de plug-in PA (PAS — option 1). Capacité STATIQUE du type
/// (pas d'un compte) : pilote les champs de creds que la console présente à la création d'un compte PA,
/// sans instancier de client. Générique — jamais un <c>if (pa is …)</c> (CLAUDE.md n°8/16).
/// </summary>
public enum PaAuthMode
{
    /// <summary>Une clé API unique chiffrée par tenant (B2Brouter, générique, factice).</summary>
    ApiKey,

    /// <summary>OAuth 2.0 client credentials : <c>client_id</c> + <c>client_secret</c> chiffrés par tenant (Super PDP).</summary>
    OAuth2ClientCredentials,
}
