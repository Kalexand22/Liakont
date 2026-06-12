namespace Liakont.Host.MultiTenancy;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Le tenant est-il SUSPENDU (statut métier <c>tenant_profiles.statut</c>, OPS03.4) ? Consommé aux
/// deux frontières d'application du statut (lot B) : refus de push agent et refus de connexion
/// console. Un tenant SANS profil est ACTIF (jamais de suspension implicite). La réponse est mise
/// en cache court (la prise d'effet d'une suspension/réactivation est bornée par le TTL — voir
/// l'implémentation) ; en cas d'échec de LECTURE, la réponse est « actif » (fail-open : une panne
/// de lecture ne coupe jamais tous les tenants — la suspension est un contrôle opérateur, pas une
/// validation fiscale).
/// </summary>
public interface ITenantSuspensionLookup
{
    /// <summary><c>true</c> si le tenant a un profil au statut « Suspendu ».</summary>
    Task<bool> IsSuspendedAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Invalide l'entrée de cache du tenant (suspension/réactivation immédiate depuis la console).</summary>
    void Invalidate(string tenantId);
}
