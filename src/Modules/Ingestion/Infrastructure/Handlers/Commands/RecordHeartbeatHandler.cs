namespace Liakont.Modules.Ingestion.Infrastructure.Handlers.Commands;

using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Domain.Entities;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Enregistre un heartbeat d'agent authentifié (F12 §3.2, §4.2) : met à jour l'état de l'agent (dernière
/// vue, version) et ajoute une entrée à l'historique append-only, dans une même transaction. Renvoie
/// l'heure serveur et la configuration courante. L'<c>AgentId</c> provient de l'identité authentifiée.
/// </summary>
public sealed class RecordHeartbeatHandler : IRequestHandler<RecordHeartbeatCommand, HeartbeatResponseDto>
{
    private readonly IAgentRegistryUnitOfWorkFactory _uowFactory;
    private readonly IAgentConfigurationProvider _configurationProvider;

    public RecordHeartbeatHandler(
        IAgentRegistryUnitOfWorkFactory uowFactory,
        IAgentConfigurationProvider configurationProvider)
    {
        _uowFactory = uowFactory;
        _configurationProvider = configurationProvider;
    }

    public async Task<HeartbeatResponseDto> Handle(RecordHeartbeatCommand request, CancellationToken cancellationToken)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var heartbeat = request.Heartbeat;

        string tenantId;
        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var agent = await uow.GetByIdAsync(request.AgentId, cancellationToken)
                ?? throw new NotFoundException("Agent", request.AgentId);

            agent.RecordHeartbeat(heartbeat.AgentVersion, receivedAt);
            await uow.UpdateAsync(agent, cancellationToken);

            var entry = HeartbeatLogEntry.Create(
                agent.Id,
                agent.TenantId,
                heartbeat.ContractVersion,
                heartbeat.AgentVersion,
                ToUtcOffset(heartbeat.SentAtUtc),
                ToNullableUtcOffset(heartbeat.LastSuccessfulSyncUtc),
                receivedAt);
            await uow.AppendHeartbeatAsync(entry, cancellationToken);

            await uow.CommitAsync(cancellationToken);
            tenantId = agent.TenantId;
        }

        var configuration = await _configurationProvider.GetForTenantAsync(tenantId, cancellationToken);
        return new HeartbeatResponseDto(receivedAt.UtcDateTime, configuration);
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToNullableUtcOffset(DateTime? value) =>
        value is null ? null : ToUtcOffset(value.Value);
}
