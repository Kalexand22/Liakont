namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record CreateServiceDefinitionCommand : IRequest<Guid>
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public string? Description { get; init; }

    public Guid? CompanyId { get; init; }

    public string? ManagerName { get; init; }

    public int? DefaultSlaHours { get; init; }

    public string? Color { get; init; }

    public string? Competences { get; init; }
}
