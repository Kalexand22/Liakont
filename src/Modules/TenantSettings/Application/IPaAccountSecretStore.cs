namespace Liakont.Modules.TenantSettings.Application;

/// <summary>
/// Lit, dans la base du tenant courant, les secrets CHIFFRÉS du compte PA ACTIF d'un type de plug-in donné.
/// Calqué sur le store chiffré des signatures (<c>ISignatureAccountStore</c>). À résoudre dans un scope
/// tenant (la connexion est routée vers la base du tenant). Renvoie <c>null</c> si aucun compte actif.
/// </summary>
public interface IPaAccountSecretStore
{
    /// <summary>Secrets chiffrés du compte PA actif (<paramref name="pluginType"/>) du tenant, ou <c>null</c>.</summary>
    Task<PaAccountSecrets?> GetActiveAsync(Guid companyId, string pluginType, CancellationToken ct = default);
}
