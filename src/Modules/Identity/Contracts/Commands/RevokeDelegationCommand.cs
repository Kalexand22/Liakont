namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record RevokeDelegationCommand : ICommand
{
    public required Guid DelegationId { get; init; }
}
