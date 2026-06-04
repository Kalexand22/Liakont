namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>Désactive un compte Plateforme Agréée du tenant courant (F12-A §4) — il n'est plus utilisé pour l'envoi.</summary>
public record DeactivatePaAccountCommand : ICommand
{
    public required Guid PaAccountId { get; init; }
}
