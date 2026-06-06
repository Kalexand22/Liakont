namespace Liakont.Modules.Pipeline.Infrastructure.Status;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts.Queries;
using MediatR;

/// <summary>
/// Handler du point de statut agent (PIP01d, ADR-0012/0014). Lit l'état DURABLE du document le plus récent pour
/// la clé <c>(sourceReference, payloadHash)</c> via <see cref="IDocumentQueries"/> (TENANT-SCOPÉ par construction :
/// la connexion EST le tenant — database-per-tenant) et dérive la sémantique terminale :
/// <list type="bullet">
///   <item>aucun Document pour la clé → <see cref="DocumentIntakeStatus.Pending"/> (reçu mais pas encore rangé,
///   ou clé inconnue) ;</item>
///   <item>un Document existe (Detected et au-delà, Issued inclus) → <see cref="DocumentIntakeStatus.Processed"/>
///   (la plateforme a pris la responsabilité du document — l'agent peut purger sa copie).</item>
/// </list>
/// Lecture seule, aucune écriture, aucune machine à états. La route répond TOUJOURS 200 (jamais 404) : une clé
/// inconnue est un Pending légitime, pas une route absente (hygiène de contrat, ADR-0012).
/// </summary>
public sealed class GetDocumentIntakeStatusHandler : IRequestHandler<GetDocumentIntakeStatusQuery, DocumentStatusResultDto>
{
    private readonly IDocumentQueries _documentQueries;

    /// <summary>Construit le handler du point de statut.</summary>
    /// <param name="documentQueries">Lectures tenant-scopées du module Documents.</param>
    public GetDocumentIntakeStatusHandler(IDocumentQueries documentQueries)
    {
        _documentQueries = documentQueries;
    }

    /// <inheritdoc />
    public async Task<DocumentStatusResultDto> Handle(GetDocumentIntakeStatusQuery request, CancellationToken cancellationToken)
    {
        var status = await _documentQueries.FindStatusBySourceReferenceAndPayloadHashAsync(
            request.SourceReference,
            request.PayloadHash,
            cancellationToken);

        // Présence du Document = la plateforme a rangé le document (Detected et au-delà) → Processed.
        // Absence = reçu mais pas encore rangé, ou clé inconnue → Pending (jamais 404, jamais terminal).
        var intakeStatus = status is null ? DocumentIntakeStatus.Pending : DocumentIntakeStatus.Processed;

        return new DocumentStatusResultDto(request.SourceReference, request.PayloadHash, intakeStatus);
    }
}
