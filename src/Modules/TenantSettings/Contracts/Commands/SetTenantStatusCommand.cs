namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Change le statut du tenant courant (F12-A §2). <see cref="Statut"/> ∈ { « Actif », « Suspendu » }
/// (insensible à la casse). Une valeur inconnue est rejetée — jamais de statut deviné.
/// </summary>
public record SetTenantStatusCommand : ICommand
{
    public required string Statut { get; init; }

    /// <summary>
    /// Société du tenant CIBLE, pour le chemin console d'administration d'instance (OPS03 : l'écran
    /// Clients agit dans le scope du tenant cible, mais l'ACTEUR — l'opérateur — porte le company_id
    /// de SON tenant). Même garde anti-injection que <see cref="ImportTenantSeedCommand.CompanyId"/> :
    /// une valeur explicite qui CONTREDIT la société réelle du tenant cible est refusée.
    /// <c>null</c> = société du contexte courant (comportement historique).
    /// </summary>
    public Guid? CompanyId { get; init; }
}
