namespace Liakont.Modules.Staging.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Staging.Contracts;

/// <summary>
/// Service de purge du staging subordonnée à la présence WORM (ADR-0014 §4). Centralise l'invariant « ne
/// JAMAIS purger sur la seule étiquette d'état <c>Issued</c> » : la purge n'a lieu que lorsque le paquet
/// d'archive WORM est PROUVÉ présent (<see cref="IArchivedDocumentProbe"/>). La transition <c>Issued</c>
/// puis l'écriture WORM (TRK05) n'étant pas atomiques, purger sur l'état seul détruirait le contenu avant
/// qu'il soit dans le coffre — d'où le sondage du blob WORM effectif.
/// </summary>
public sealed class StagingPurgeService : IStagingPurgeService
{
    private readonly IPayloadStagingStore _store;
    private readonly IArchivedDocumentProbe _archivedDocumentProbe;

    /// <summary>Construit le service à partir du magasin de staging et de la sonde de présence WORM.</summary>
    /// <param name="store">Le magasin de staging (purge).</param>
    /// <param name="archivedDocumentProbe">La sonde de présence du paquet d'archive WORM.</param>
    public StagingPurgeService(IPayloadStagingStore store, IArchivedDocumentProbe archivedDocumentProbe)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _archivedDocumentProbe = archivedDocumentProbe ?? throw new ArgumentNullException(nameof(archivedDocumentProbe));
    }

    /// <inheritdoc />
    public async Task<bool> PurgeIfArchivedAsync(
        StagedPayloadKey key,
        ArchivedDocumentLocator locator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(locator);

        if (!await _archivedDocumentProbe.IsArchivedAsync(locator, cancellationToken))
        {
            // Paquet WORM non encore présent (ex. entre la transition Issued et l'écriture TRK05) : le
            // contenu n'est PAS prouvé préservé → staging CONSERVÉ (ADR-0014 §4).
            return false;
        }

        await _store.PurgeAsync(key, cancellationToken);
        return true;
    }
}
