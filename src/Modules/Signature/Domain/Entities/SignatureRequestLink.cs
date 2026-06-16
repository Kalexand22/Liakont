namespace Liakont.Modules.Signature.Domain.Entities;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Liaison tenant-scopée entre une référence de demande côté fournisseur et le DOCUMENT visé (ADR-0029 §5).
/// Persistée à l'émission d'une demande de signature ; relue par le drain pour rapatrier la preuve dans le
/// coffre WORM via <c>Archive.Contracts</c> (le webhook ne porte que la référence fournisseur, pas le numéro
/// ni la date d'émission du document). Aucune dépendance au module Documents : le numéro et la date sont
/// recopiés ici à l'émission (le <c>purpose</c> est une chaîne opaque — aucun couplage à DocumentApproval).
/// </summary>
public sealed record SignatureRequestLink
{
    /// <summary>Tenant propriétaire (clé d'isolation <c>company_id</c>).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Type de fournisseur (clé de registre, ex. « Yousign »).</summary>
    public required string ProviderType { get; init; }

    /// <summary>Référence de la demande côté fournisseur (clé de corrélation du webhook).</summary>
    public required string ProviderReference { get; init; }

    /// <summary>Identifiant du document signé (clé du paquet d'archive WORM).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Numéro du document (localise le répertoire du paquet d'archive).</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Date d'émission du document (année/mois du répertoire du paquet d'archive).</summary>
    public required DateOnly IssueDate { get; init; }

    /// <summary>Objet de validation (chaîne opaque, traçabilité) — jamais interprété ici.</summary>
    public string? Purpose { get; init; }

    /// <summary>Niveau de preuve demandé (paramétrage tenant — jamais un défaut produit).</summary>
    public SignatureLevel RequestedLevel { get; init; }

    /// <summary>Horodatage UTC de l'émission de la demande.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
}
