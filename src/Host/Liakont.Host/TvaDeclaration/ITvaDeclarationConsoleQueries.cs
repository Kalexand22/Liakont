namespace Liakont.Host.TvaDeclaration;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Façade Host (composition EN LECTURE) de la page « TVA / Déclaration » (aide à la déclaration de TVA sous le
/// régime de la marge, L2). Isole la page de la couche Query du module Pipeline et projette le DTO en modèle de
/// présentation (avec totaux). Tenant-scopée par construction (la couche Query lit la base du tenant courant —
/// CLAUDE.md n°9/17).
/// </summary>
internal interface ITvaDeclarationConsoleQueries
{
    /// <summary>
    /// Récap de la marge à déclarer du tenant courant pour une période année-mois (<c>"yyyy-MM"</c>) — un filtre
    /// de DATE pur sur le jour d'émission (jamais une règle fiscale). Une période vide ou nulle ne filtre pas.
    /// </summary>
    Task<TvaDeclarationViewModel> GetDeclarationAsync(string? period, CancellationToken cancellationToken = default);
}
