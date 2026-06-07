namespace Liakont.Modules.Staging.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Service de purge du staging subordonnée à la présence WORM (ADR-0014 §4). Centralise l'invariant « ne
/// JAMAIS purger sur la seule étiquette d'état <c>Issued</c> » en UN seul endroit : la purge n'a lieu que
/// lorsque le paquet d'archive WORM est PROUVÉ présent (<see cref="IArchivedDocumentProbe"/>), donc le
/// contenu est préservé ailleurs avant que le filet de staging ne disparaisse. Consommé par le pipeline
/// (PIP01) après un envoi réussi, via cette surface Contracts.
/// </summary>
public interface IStagingPurgeService
{
    /// <summary>
    /// Purge l'entrée de staging UNIQUEMENT si le paquet d'archive WORM du document est effectivement présent
    /// (ADR-0014 §4). Sinon, l'entrée est CONSERVÉE (le contenu n'est pas encore prouvé préservé).
    /// </summary>
    /// <param name="key">La clé tenant-scopée de l'entrée de staging.</param>
    /// <param name="locator">La localisation du paquet d'archive WORM à sonder.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns><c>true</c> si l'entrée a été purgée (WORM confirmé), <c>false</c> si conservée (WORM absent).</returns>
    Task<bool> PurgeIfArchivedAsync(StagedPayloadKey key, ArchivedDocumentLocator locator, CancellationToken cancellationToken = default);
}
