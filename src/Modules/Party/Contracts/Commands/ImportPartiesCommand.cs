namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;
using Stratum.Modules.Party.Contracts.DTOs;

public record ImportPartiesCommand : ICommand<ImportPartiesResultDto>
{
    public required IReadOnlyList<ImportPartyRow> Rows { get; init; }

    public bool DryRun { get; init; }
}
