namespace Liakont.Host.Clients;

using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>Résultat de l'import de seed (récapitulatif des composants importés).</summary>
internal sealed record ClientSeedResult(
    ClientActionStatus Status,
    string? Message = null,
    ImportTenantSeedResult? Imported = null);
