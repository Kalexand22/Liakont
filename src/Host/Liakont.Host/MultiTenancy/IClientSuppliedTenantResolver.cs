namespace Liakont.Host.MultiTenancy;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Marque un <see cref="ITenantResolver"/> dont la valeur provient d'une donnée <b>CLIENT-FOURNIE</b> de
/// la requête (sous-domaine de l'hôte, en-tête HTTP <c>X-Tenant-Id</c>) — par opposition aux voies dérivées
/// du <b>jeton</b> (<c>company_id</c>, issuer OIDC). En realm Keycloak unique (ADR-0021 §2c) ces voies ne
/// sont plus autoritaires : le cross-check d'isolation (RLM03, INV-0021-4) les confronte au tenant du jeton
/// et REJETTE (403) tout indice client qui le <b>contredit</b> — jamais servi silencieusement comme tenant
/// du jeton.
/// <para>
/// Tout NOUVEAU résolveur dont la source est client-contrôlée DOIT porter ce marqueur : il sera alors
/// automatiquement couvert par le cross-check, fermant la fuite silencieuse qu'introduirait la
/// réintroduction d'un en-tête « de confiance ».
/// </para>
/// </summary>
internal interface IClientSuppliedTenantResolver : ITenantResolver
{
}
