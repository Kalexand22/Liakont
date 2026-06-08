namespace Liakont.Host.Documents;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.Queries;

/// <summary>
/// Implémentation de <see cref="IDocumentDetailConsoleQueries"/> : assemble la vue détail à partir des
/// lectures du module Documents (<see cref="IDocumentQueries.GetByIdAsync"/> + <c>GetEventsAsync</c> +
/// <c>GetArchiveReferenceAsync</c>), à l'identique de l'endpoint <c>GET /api/v1/documents/{id}</c>
/// (DocumentsEndpointMapping). Aucune règle métier : la projection « dernier événement de blocage » et la
/// présence d'archive sont de la PRÉSENTATION de la piste d'audit en lecture (pas de fiscalité, pas de
/// machine à états — celles-ci restent dans les handlers/domaines). Tenant-scopée (la connexion EST le tenant).
/// </summary>
internal sealed class DocumentDetailConsoleQueryService : IDocumentDetailConsoleQueries
{
    // Noms d'événements/d'état du domaine Documents, référencés en CHAÎNE (un projet du Host ne référence pas
    // le Domain d'un module — frontière de dépendance) ; mêmes valeurs que DocumentsEndpointMapping.
    private const string BlockedEventType = "DocumentBlocked";
    private const string BlockedDocumentState = "Blocked";

    private readonly IDocumentQueries _documents;

    public DocumentDetailConsoleQueryService(IDocumentQueries documents)
    {
        _documents = documents;
    }

    public async Task<DocumentDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documents.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var events = await _documents.GetEventsAsync(id, cancellationToken).ConfigureAwait(false);
        var archive = await _documents.GetArchiveReferenceAsync(id, cancellationToken).ConfigureAwait(false);

        // Motif de blocage = le DERNIER événement DocumentBlocked, et seulement si le document est ENCORE
        // bloqué (sinon le motif est périmé → message trompeur). Tri stable par horodatage puis Id.
        var blockingReason = string.Equals(document.State, BlockedDocumentState, StringComparison.Ordinal)
            ? events
                .Where(e => string.Equals(e.EventType, BlockedEventType, StringComparison.Ordinal))
                .OrderByDescending(e => e.TimestampUtc)
                .ThenByDescending(e => e.Id)
                .Select(e => e.Detail)
                .FirstOrDefault()
            : null;

        return new DocumentDetailViewModel
        {
            Document = document,
            Events = events,
            BlockingReason = blockingReason,
            Archive = archive,
            IsArchived = archive is not null,
        };
    }
}
