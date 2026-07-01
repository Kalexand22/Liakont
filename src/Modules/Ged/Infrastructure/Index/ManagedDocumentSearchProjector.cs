namespace Liakont.Modules.Ged.Infrastructure.Index;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Application.Index;
using Liakont.Modules.Ged.Contracts.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Projection ASYNCHRONE de l'index de recherche GED (F19 §6.1, item GED08). SECOND consommateur durable de
/// <see cref="ManagedDocumentReceivedV1"/> : le dispatcher du socle invoque les consommateurs dans l'ORDRE
/// d'enregistrement (voir <c>GedModuleRegistration</c>), ce projecteur est enregistré APRÈS l'indexeur
/// (<c>ManagedDocumentReceivedConsumer</c>), donc il tourne une fois l'index (titre + liens d'axe) committé et
/// recalcule le <c>search_vector</c> du document dans la table dérivée <c>ged_index.document_search</c>.
/// </summary>
/// <remarks>
/// <para>DÉCOUPLAGE (§6.1) : le recalcul du plein-texte est fait ici, hors de la transaction d'indexation, pour ne
/// pas coupler la latence d'ingestion à celle de l'indexation FTS (asymétrie assumée vs le <c>search_vector</c>
/// inline des entités). La table <c>document_search</c> est un DÉRIVÉ reconstructible : la projection ne lit que la
/// base tenant (jamais le staging), donc le même chemin sert au rebuild total et au backfill (GED10).</para>
/// <para>IDEMPOTENCE (livraison at-least-once) : <see cref="IDocumentSearchIndex.RefreshDocumentAsync"/> fait un
/// UPSERT — un replay réécrit la même ligne. Tenant-scope via <see cref="ITenantScopeFactory"/> (seam du Host,
/// comme l'indexeur) à partir du slug porté par l'événement.</para>
/// </remarks>
internal sealed partial class ManagedDocumentSearchProjector : IIntegrationEventConsumer<ManagedDocumentReceivedV1>
{
    private readonly ITenantScopeFactory _scopeFactory;
    private readonly ILogger<ManagedDocumentSearchProjector> _logger;

    public ManagedDocumentSearchProjector(ITenantScopeFactory scopeFactory, ILogger<ManagedDocumentSearchProjector> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(IntegrationEvent<ManagedDocumentReceivedV1> integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;

        await using var scope = _scopeFactory.Create(payload.TenantId);
        var index = scope.Services.GetRequiredService<IDocumentSearchIndex>();
        await index.RefreshDocumentAsync(payload.ManagedDocumentId, cancellationToken);

        LogProjected(_logger, payload.ManagedDocumentId, payload.TenantId);
    }

    [LoggerMessage(EventId = 7320, Level = LogLevel.Information,
        Message = "Projection FTS GED : search_vector recalculé pour le document {ManagedDocumentId} (tenant « {TenantId} »).")]
    private static partial void LogProjected(ILogger logger, Guid managedDocumentId, string tenantId);
}
