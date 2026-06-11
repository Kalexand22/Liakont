namespace Liakont.Host.PaAccounts;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition de la page « Comptes plateforme agréée » (FIX01c) : assemble les comptes PA du tenant
/// courant + les types de plug-ins enregistrés (lecture), et applique les mutations (création, édition,
/// saisie/rotation de clé, désactivation) via les commandes TenantSettings. Isole l'accès au module hors
/// de la page Blazor (la page reste présentationnelle — CLAUDE.md n°19) et rend l'ensemble testable.
/// Tenant-scopé : la société est résolue côté handler (jamais un paramètre client — CLAUDE.md n°9).
/// La clé API saisie est passée telle quelle à la commande (chiffrée par le handler) — jamais journalisée
/// ni réaffichée (CLAUDE.md n°10).
/// </summary>
internal interface IPaAccountConsoleService
{
    /// <summary>Assemble les comptes PA du tenant courant et les types de plug-ins enregistrés (proposés en création).</summary>
    Task<PaAccountConsoleModel> GetModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée un compte PA pour le tenant courant (<c>AddPaAccountCommand</c>). Retourne l'id du compte.
    /// La clé API saisie (facultative) est chiffrée par le handler ; un doublon (plug-in, environnement)
    /// lève <see cref="Stratum.Common.Abstractions.Exceptions.ConflictException"/> (message porté par la page).
    /// </summary>
    Task<Guid> CreateAsync(PaAccountFormModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Met à jour un compte PA existant (<c>UpdatePaAccountCommand</c>) : environnement, identifiants, et
    /// rotation de clé si une valeur est saisie (vide = clé inchangée). Le type de plug-in n'est pas modifiable.
    /// Compte introuvable → <see cref="Stratum.Common.Abstractions.Exceptions.NotFoundException"/>.
    /// </summary>
    Task UpdateAsync(PaAccountFormModel model, CancellationToken cancellationToken = default);

    /// <summary>Désactive un compte PA (<c>DeactivatePaAccountCommand</c>) — il n'est plus utilisé pour l'envoi.</summary>
    Task DeactivateAsync(Guid paAccountId, CancellationToken cancellationToken = default);
}
