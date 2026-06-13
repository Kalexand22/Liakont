namespace Liakont.Modules.Documents.Contracts.Queries;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;

/// <summary>
/// Lecture SEULE des compteurs de documents par état sur un périmètre. Interface SÉGRÉGÉE de
/// <see cref="IDocumentQueries"/> (précédent FIX212) : les consommateurs de synthèse (tableau de
/// bord) n'ont besoin que de la répartition par état — pas de la liste paginée ni de ses requêtes
/// annexes (liste + total), qui seraient calculées puis jetées. L'ajout ici n'alourdit pas le
/// contrat principal (nombreux fakes).
/// </summary>
public interface IDocumentStateCountQueries
{
    /// <summary>
    /// Compteurs par état des documents du tenant courant correspondant au PÉRIMÈTRE du filtre
    /// (dates d'émission, type, recherche). <see cref="DocumentListFilter.State"/> et la pagination
    /// sont ignorés : la répartition couvre TOUS les états du périmètre.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetStateCountsAsync(
        DocumentListFilter filter,
        CancellationToken cancellationToken = default);
}
