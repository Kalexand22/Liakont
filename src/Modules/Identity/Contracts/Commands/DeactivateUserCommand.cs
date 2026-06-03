namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record DeactivateUserCommand : ICommand
{
    public required Guid UserId { get; init; }
}
