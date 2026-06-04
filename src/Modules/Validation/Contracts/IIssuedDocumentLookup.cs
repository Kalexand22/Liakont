namespace Liakont.Modules.Validation.Contracts;

/// <summary>
/// Port (inversion de dépendance) par lequel une règle de validation interroge le module Documents
/// pour savoir si un numéro de document a déjà été émis pour un tenant donné (anti-doublon, F04 §3.3).
/// Le module Validation DÉCLARE ce besoin dans ses Contracts ; l'implémentation réelle (lecture des
/// documents émis du tenant) est fournie par le module Documents/Tracking (lot TRK, item TRK03).
/// Tant qu'elle n'existe pas, un faux d'essai suffit (VAL03). La frontière Contracts-only est ainsi
/// respectée : Validation ne référence jamais le Domain d'un autre module (module-rules.md §3).
/// </summary>
public interface IIssuedDocumentLookup
{
    /// <summary>
    /// Indique si un document portant ce numéro a déjà été émis pour ce tenant. Requête
    /// TENANT-SCOPÉE (CLAUDE.md n°9) : <paramref name="companyId"/> est obligatoire et borne la
    /// recherche au seul tenant concerné.
    /// </summary>
    /// <param name="companyId">Tenant (clé d'isolation). Jamais <c>Guid.Empty</c>.</param>
    /// <param name="documentNumber">Numéro de document à rechercher (EN 16931 BT-1).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns><c>true</c> si un document de même numéro a déjà été émis pour ce tenant.</returns>
    Task<bool> IsAlreadyIssuedAsync(Guid companyId, string documentNumber, CancellationToken cancellationToken = default);
}
