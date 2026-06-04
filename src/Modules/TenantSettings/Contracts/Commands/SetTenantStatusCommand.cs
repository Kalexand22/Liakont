namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Change le statut du tenant courant (F12-A §2). <see cref="Statut"/> ∈ { « Actif », « Suspendu » }
/// (insensible à la casse). Une valeur inconnue est rejetée — jamais de statut deviné.
/// </summary>
public record SetTenantStatusCommand : ICommand
{
    public required string Statut { get; init; }
}
