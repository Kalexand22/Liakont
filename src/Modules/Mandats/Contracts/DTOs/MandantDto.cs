namespace Liakont.Modules.Mandats.Contracts.DTOs;

/// <summary>
/// Vue de lecture d'un mandant (F15 §2.2). DTO pur (aucune logique). Les valeurs sont du paramétrage
/// tenant — aucune donnée client n'est embarquée dans le code (CLAUDE.md n°7, INV-MANDATS-5).
/// </summary>
public sealed record MandantDto
{
    /// <summary>Identifiant technique du mandant.</summary>
    public required Guid Id { get; init; }

    /// <summary>Référence métier du mandant (tenant-scopée).</summary>
    public required string Reference { get; init; }

    /// <summary>Raison sociale du mandant.</summary>
    public required string RaisonSociale { get; init; }

    /// <summary>Numéro de TVA du mandant (BT-31) ; <c>null</c> si non renseigné.</summary>
    public string? SellerVatNumber { get; init; }

    /// <summary>SIREN du mandant (BT-30 en flux 389).</summary>
    public required string Siren { get; init; }

    /// <summary>Préfixe de numérotation propre au mandant (F15 §1.4).</summary>
    public required string NumberingPrefix { get; init; }
}
