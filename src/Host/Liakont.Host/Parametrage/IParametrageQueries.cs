namespace Liakont.Host.Parametrage;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;

/// <summary>
/// Composition en LECTURE de la page Paramétrage du tenant (WEB04b) : assemble, pour le tenant
/// courant, le profil, les paramètres fiscaux, le résumé de la table TVA, les comptes PA et les
/// agents ; et déclenche à la demande la vérification d'intégrité du coffre d'archive (API03/TRK06).
/// Isole l'assemblage et l'appel d'intégrité hors de la page Blazor (la page reste présentationnelle,
/// CLAUDE.md n°19) et les rend testables unitairement. Tenant-scopée (CLAUDE.md n°9).
/// </summary>
internal interface IParametrageQueries
{
    /// <summary>Assemble le modèle de la page Paramétrage pour le tenant courant.</summary>
    Task<ParametrageViewModel> GetParametrageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Vérifie l'intégrité de TOUT le coffre d'archive du tenant courant (chaîne + ancrages) et
    /// produit le rapport. Action à la demande déclenchée par l'opérateur depuis la page.
    /// </summary>
    Task<ArchiveVerificationReport> VerifyArchiveIntegrityAsync(CancellationToken cancellationToken = default);
}
