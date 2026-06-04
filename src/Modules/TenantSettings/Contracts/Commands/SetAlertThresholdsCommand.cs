namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>Définit (upsert) les seuils d'alerte de supervision du tenant courant (F12-A §6).</summary>
public record SetAlertThresholdsCommand : ICommand
{
    public required int AgentSilentHours { get; init; }

    public required int MissedRunHours { get; init; }

    public required int PushQueueMaxItems { get; init; }

    public required int PushQueueMaxAgeHours { get; init; }

    public required int BlockedDocumentsDays { get; init; }

    public required int PaRejectionsDays { get; init; }

    public bool AlertTenantContact { get; init; }
}
