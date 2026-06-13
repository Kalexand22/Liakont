namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Crée ou met à jour (upsert) le profil du tenant courant (F12-A §2). Le tenant est résolu par
/// le contexte (jamais passé par l'appelant — tenant-scoping, CLAUDE.md n°9). Retourne l'id du profil.
/// </summary>
public record SaveTenantProfileCommand : ICommand<Guid>
{
    /// <summary>
    /// Société du tenant CIBLE, pour le chemin console de PROVISIONING (OPS03, assistant « Nouveau
    /// client » : aucun profil n'existe encore et l'acteur — l'opérateur — porte le company_id de SON
    /// tenant, jamais celui du tenant cible). Même sémantique de garde que
    /// <see cref="ImportTenantSeedCommand.CompanyId"/>. <c>null</c> = société du contexte courant.
    /// </summary>
    public Guid? CompanyId { get; init; }

    public required string Siren { get; init; }

    public required string RaisonSociale { get; init; }

    public required string Street { get; init; }

    public required string PostalCode { get; init; }

    public required string City { get; init; }

    public required string Country { get; init; }

    public string? ContactEmailAlerte { get; init; }
}
