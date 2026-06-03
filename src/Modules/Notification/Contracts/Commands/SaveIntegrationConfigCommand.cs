namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record SaveIntegrationConfigCommand : IRequest<Guid>
{
    public required string IntegrationType { get; init; }

    public required string ConfigJson { get; init; }

    public required bool IsEnabled { get; init; }

    public required Guid CompanyId { get; init; }
}
