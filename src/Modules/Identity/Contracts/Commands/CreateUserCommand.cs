namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record CreateUserCommand : ICommand<Guid>
{
    public required string Username { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Keycloak subject ID. Required — user creation now goes through the IdP.</summary>
    public required string ExternalId { get; init; }

    public Guid? PartyId { get; init; }
}
