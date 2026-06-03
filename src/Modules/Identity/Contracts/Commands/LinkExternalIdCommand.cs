namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Links an existing user (identified by UserId) to an external identity provider
/// by setting the ExternalId. Used to back-fill pre-OIDC admin users at startup.
/// </summary>
public record LinkExternalIdCommand : ICommand
{
    public required Guid UserId { get; init; }

    public required string ExternalId { get; init; }
}
