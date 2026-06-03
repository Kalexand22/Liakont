namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record UpdateAgentProfileCommand : ICommand
{
    public required Guid AgentProfileId { get; init; }

    public string? ServiceCode { get; init; }

    public string? Title { get; init; }

    public string? Phone { get; init; }

    public string? OfficeLocation { get; init; }

    public DateOnly? HireDate { get; init; }

    public string? Notes { get; init; }
}
