namespace Liakont.Host.MultiTenancy;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Marque un <see cref="ITenantResolver"/> dont la valeur provient d'un canal <b>CLIENT-FOURNI
/// DÉLIBÉRÉ</b> de la requête — à ce jour, l'en-tête HTTP <c>X-Tenant-Id</c>. En realm Keycloak unique
/// (ADR-0021 §2c) ces canaux ne sont plus autoritaires : le cross-check d'isolation (RLM03, INV-0021-4)
/// les confronte au tenant du jeton et REJETTE (403) tout indice client qui le <b>contredit</b> — jamais
/// servi silencieusement comme tenant du jeton.
/// <para>
/// Le sous-domaine de l'hôte est <b>exclu</b> en topologie mono-host SaaS mutualisé (ADR-0021
/// §Conséquences) : il est incident, non sous contrôle délibéré du client, et provoquerait un faux-403
/// (ex. « app » dans <c>app.liakont.fr</c> serait traité comme un identifiant de tenant).
/// </para>
/// <para>
/// Tout NOUVEAU résolveur dont la source est un canal client-contrôlé DÉLIBÉRÉ DOIT porter ce marqueur :
/// il sera alors automatiquement couvert par le cross-check, fermant la fuite silencieuse
/// qu'introduirait la réintroduction d'un en-tête « de confiance ».
/// </para>
/// </summary>
internal interface IClientSuppliedTenantResolver : ITenantResolver
{
}
