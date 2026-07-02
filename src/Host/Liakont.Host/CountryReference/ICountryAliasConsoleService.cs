namespace Liakont.Host.CountryReference;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Service d'assemblage de l'écran « Référentiel pays » (ADR-0038, Lot 4). LECTURE seule du référentiel de
/// correspondance pays cross-instance (<see cref="Liakont.Modules.Reference.Contracts.ICountryAliasReferential"/>)
/// projetée en lignes d'affichage — AUCUNE logique métier ni mutation ici : les écritures passent par les
/// commandes MediatR <c>UpsertCountryAliasCommand</c> / <c>RemoveCountryAliasCommand</c> (validées et journalisées
/// côté handler). Isole l'accès au module hors de la page (frontière inter-modules par Contracts, CLAUDE.md n°14).
/// </summary>
public interface ICountryAliasConsoleService
{
    /// <summary>Liste les correspondances du référentiel, triées par code source (projection d'affichage).</summary>
    Task<IReadOnlyList<CountryAliasRow>> ListAsync(CancellationToken cancellationToken = default);
}
