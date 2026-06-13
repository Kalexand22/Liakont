namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Résolution AUTORITAIRE du tenant courant à partir du <c>company_id</c> porté par le jeton
/// (ADR-0021 §2c) : en realm Keycloak unique, le mapping <c>realm → tenant</c> ne distingue plus rien,
/// la voie autoritaire devient <c>company_id(jeton) → outbox.tenants.company_id → tenant</c>.
/// <para>
/// Synchrone à dessein : consommé par un <c>ITenantResolver</c> (lui-même synchrone) sur le chemin
/// chaud de résolution du tenant. La requête est indexée par la contrainte UNIQUE de V017.
/// </para>
/// </summary>
public interface ICompanyTenantLookup
{
    /// <summary>
    /// Retourne l'identifiant du tenant dont <c>outbox.tenants.company_id</c> vaut <paramref name="companyId"/>,
    /// ou <c>null</c> si aucun tenant ne porte cette société.
    /// </summary>
    string? FindTenantId(Guid companyId);
}
