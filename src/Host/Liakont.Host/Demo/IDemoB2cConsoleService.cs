namespace Liakont.Host.Demo;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition EN LECTURE de l'écran « Démo e-reporting B2C — Essentiel » (B2C04) : assemble, pour le tenant
/// courant, les déclarations 10.3 et leur état de bout en bout (transmis/accusé, bloqué régime non mappé,
/// présence du lien reporting↔pièces B2C03, export). Isole l'accès aux modules hors de la page Blazor (la page
/// reste présentationnelle — CLAUDE.md n°19). PURE COMPOSITION de contrats existants (lecture des documents +
/// store de liens reporting↔pièces) : aucune logique métier, aucun second chemin d'envoi. Tenant-scopé : la
/// société est résolue côté tenant courant (jamais un paramètre client — CLAUDE.md n°9).
/// </summary>
internal interface IDemoB2cConsoleService
{
    /// <summary>Assemble les déclarations 10.3 du tenant courant et leur état de démo Essentiel.</summary>
    Task<DemoB2cViewModel> GetAsync(CancellationToken cancellationToken = default);
}
