namespace Liakont.Host.InstanceEmail;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Service console (Host) de la configuration d'envoi d'emails d'instance (ADR-0039). Orchestre le
/// chiffrement (au save), la lecture masquée (booléens <c>Has*</c>) et l'envoi d'un email de test — le
/// monopole du chiffrement/déchiffrement reste au Host (précédent <c>GeneriqueAccountResolver</c>,
/// CLAUDE.md n°6/14). Aucune logique métier dans la page : elle délègue à ce service (review n°19).
/// </summary>
public interface IInstanceEmailConfigService
{
    /// <summary>Lit la configuration d'instance (secrets masqués <c>Has*</c>) ; valeurs par défaut si aucune.</summary>
    Task<InstanceEmailConfigViewModel> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Chiffre les secrets saisis (conserve les secrets vides) puis persiste le ciphertext.</summary>
    Task SaveAsync(InstanceEmailConfigInput input, CancellationToken cancellationToken = default);

    /// <summary>Envoie un email de test avec la configuration enregistrée ; rend un résultat, ne lève jamais.</summary>
    Task<EmailTestResult> SendTestAsync(string recipient, CancellationToken cancellationToken = default);
}
