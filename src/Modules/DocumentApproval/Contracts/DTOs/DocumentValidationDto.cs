namespace Liakont.Modules.DocumentApproval.Contracts.DTOs;

/// <summary>
/// Vue de lecture (seule) d'une tentative de validation de document (ADR-0028 §2/§3). DTO pur. <see cref="State"/>
/// et <see cref="ProofLevel"/> sont exposés sous forme de <b>nom</b> (pas l'enum de domaine) pour garder la
/// surface <c>Contracts</c> sans dépendance sur <c>Domain</c>. La <b>Règle de gate</b> (ouverture effective) est
/// portée par le Domain (<c>ApprovalGate</c>) et câblée par les ports de purpose (SIG06) — pas dupliquée ici.
/// </summary>
public sealed record DocumentValidationDto
{
    /// <summary>Document concerné (référence lâche — aucun couplage de schéma cross-module).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Finalité de validation (clé de couplage).</summary>
    public required ValidationPurpose Purpose { get; init; }

    /// <summary>Numéro de tentative (≥ 1) ; le gate lit la tentative la plus récente (ADR-0028 §6).</summary>
    public required int Attempt { get; init; }

    /// <summary>État courant (nom de <c>ValidationState</c>, machine fermée 7 états).</summary>
    public required string State { get; init; }

    /// <summary>Niveau de preuve attaché à l'agrégat mono-partie (nom de <c>SignatureLevel</c>) ; le N-parties porte le niveau PAR slot.</summary>
    public required string ProofLevel { get; init; }

    /// <summary>Une acceptation expresse explicite a été enregistrée (condition 3 du gate self-billing, ADR-0028 §5).</summary>
    public required bool ExpressAcceptanceRecorded { get; init; }

    /// <summary>Échéance (UTC) de bascule tacite / timeout ; <c>null</c> = bascule tacite impossible.</summary>
    public DateTimeOffset? DeadlineUtc { get; init; }

    /// <summary>L'état est terminal (aucune transition possible).</summary>
    public required bool IsTerminal { get; init; }

    /// <summary>Slots d'approbation N-parties (vide pour un purpose mono-partie).</summary>
    public IReadOnlyList<ApprovalSlotDto> Slots { get; init; } = [];
}
