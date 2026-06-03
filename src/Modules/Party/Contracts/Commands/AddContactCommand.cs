namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record AddContactCommand : ICommand<Guid>
{
    public required Guid PartyId { get; init; }

    public required string ContactType { get; init; }

    public string? Label { get; init; }

    public required string Value { get; init; }

    public bool IsPrimary { get; init; }
}
