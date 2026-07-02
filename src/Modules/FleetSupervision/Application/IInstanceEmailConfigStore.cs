namespace Liakont.Modules.FleetSupervision.Application;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Magasin de la configuration d'envoi d'emails d'INSTANCE (ligne singleton, base système — ADR-0039),
/// via <c>ISystemConnectionFactory</c> (jamais une connexion tenant ; précédent <c>PostgresFleetStore</c>).
/// <para>
/// Le magasin ne stocke/retourne que du <see cref="InstanceEmailConfig"/> — <em>ciphertext</em> ou non-secrets,
/// jamais de secret en clair (le chiffrement/déchiffrement est le monopole du Host, CLAUDE.md n°6/14). Il
/// n'appelle jamais <c>ISecretProtector</c> (frontière : cette abstraction vit dans <c>TenantSettings.Application</c>,
/// consommable seulement côté Host/TenantSettings).
/// </para>
/// </summary>
public interface IInstanceEmailConfigStore
{
    /// <summary>Lit la ligne de config d'instance (ciphertext) ; <c>null</c> si aucune n'a encore été enregistrée.</summary>
    Task<InstanceEmailConfig?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Insère ou remplace la ligne singleton de config d'instance (upsert idempotent).</summary>
    Task UpsertAsync(InstanceEmailConfig config, CancellationToken cancellationToken = default);
}
