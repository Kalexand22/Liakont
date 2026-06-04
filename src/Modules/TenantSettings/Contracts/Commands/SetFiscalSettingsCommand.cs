namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Définit (upsert) le paramétrage fiscal du tenant courant (F12-A §3). Tous les champs sont
/// optionnels : <c>null</c> = décision de l'expert-comptable en attente = suspension (jamais de
/// défaut). <see cref="OperationCategory"/> ∈ { « LivraisonBiens », « PrestationServices »,
/// « Mixte » } ou <c>null</c> ; une valeur inconnue est rejetée. <see cref="ReportingFrequency"/>
/// est stocké opaque (énumération non figée — F12-A §3.3, CLAUDE.md n°2).
/// </summary>
public record SetFiscalSettingsCommand : ICommand
{
    public bool? VatOnDebits { get; init; }

    public string? OperationCategory { get; init; }

    public string? ReportingFrequency { get; init; }
}
