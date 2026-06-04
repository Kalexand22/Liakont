namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Définit (upsert) la planification d'extraction du tenant courant (F12-A §5).
/// <see cref="Hours"/> au format <c>HH:mm</c> ; poussée vers l'agent via le heartbeat (AGT03).
/// </summary>
public record SetExtractionScheduleCommand : ICommand
{
    public required IReadOnlyList<string> Hours { get; init; }

    public bool CatchUpOnStart { get; init; }
}
