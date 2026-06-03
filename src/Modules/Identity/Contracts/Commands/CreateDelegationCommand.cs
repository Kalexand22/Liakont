namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record CreateDelegationCommand : ICommand<Guid>
{
    public required Guid DelegatorId { get; init; }

    public required Guid DelegateId { get; init; }

    public required string Scope { get; init; }

    public required DateTimeOffset ValidFrom { get; init; }

    public required DateTimeOffset ValidUntil { get; init; }

    public string? Reason { get; init; }
}
