namespace Liakont.Modules.TenantSettings.Application;

/// <summary>
/// « Purposes » Data Protection des secrets d'un compte PA : chaque secret est chiffré sous un purpose
/// DISTINCT (isolation cryptographique — un texte chiffré pour un purpose ne peut pas être déchiffré sous
/// un autre). Versionnés <c>.v1</c> pour permettre une rotation future. Le purpose « clé API » correspond
/// au comportement historique de <see cref="ISecretProtector.Protect(string)"/> (rétrocompatibilité des
/// secrets déjà chiffrés — ne JAMAIS modifier cette chaîne).
/// </summary>
public static class PaAccountSecretPurposes
{
    /// <summary>Purpose de la clé API (auth ApiKey). Identique à l'historique : ne pas changer (données existantes).</summary>
    public const string ApiKey = "Liakont.TenantSettings.PaAccount.ApiKey.v1";

    /// <summary>Purpose du « client_id » OAuth2 (auth OAuth2ClientCredentials).</summary>
    public const string ClientId = "Liakont.TenantSettings.PaAccount.ClientId.v1";

    /// <summary>Purpose du « client_secret » OAuth2 (auth OAuth2ClientCredentials).</summary>
    public const string ClientSecret = "Liakont.TenantSettings.PaAccount.ClientSecret.v1";
}
