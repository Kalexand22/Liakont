namespace Liakont.Host.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Host.Components;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Projection de présentation du DÉTAIL d'une transmission e-reporting B2C (BUG-22). PRÉSENTATION pure : le motif
/// PA est restitué LISIBLEMENT (codes + messages, jamais de JSON brut — F10 §1) via
/// <see cref="PaResponseSnapshotFormatter"/> ; la famille de chaque pièce est dérivée de sa référence source
/// (<see cref="DocumentFamilyDisplay"/>). Aucune logique fiscale (CLAUDE.md n°2). Tenant-scopée en amont.
/// </summary>
internal sealed record B2cMarginEmissionDetailViewModel
{
    public required Guid EmissionBatchId { get; init; }

    public required DateOnly AggregateDate { get; init; }

    public required string Currency { get; init; }

    public required string Category { get; init; }

    public required string Role { get; init; }

    /// <summary>NOM du statut courant (traduit/coloré à l'affichage via <see cref="B2cMarginEmissionStatusDisplay"/>).</summary>
    public required string Status { get; init; }

    /// <summary>Identifiant serveur côté PA, « — » si l'agrégat n'est pas encore émis.</summary>
    public required string PaEmissionId { get; init; }

    /// <summary>Message opérateur de la dernière issue non terminale, « — » si absent.</summary>
    public required string Detail { get; init; }

    /// <summary>Motif PA restitué en lignes lisibles (vide si aucun snapshot exploitable) — jamais de JSON brut.</summary>
    public required IReadOnlyList<string> PaResponseLines { get; init; }

    public required DateTimeOffset LastActivityUtc { get; init; }

    /// <summary>Pièces composant l'agrégat (lien vers chaque document + famille dérivée).</summary>
    public required IReadOnlyList<B2cMarginEmissionDetailDocumentRow> Documents { get; init; }

    /// <summary>Projette le DTO du module Pipeline en modèle de présentation (formatage uniquement).</summary>
    public static B2cMarginEmissionDetailViewModel FromDto(B2cMarginEmissionDetailDto dto) => new()
    {
        EmissionBatchId = dto.EmissionBatchId,
        AggregateDate = dto.AggregateDate,
        Currency = dto.CurrencyCode,
        Category = dto.Category,
        Role = dto.Role,
        Status = dto.Status,
        PaEmissionId = string.IsNullOrWhiteSpace(dto.PaEmissionId) ? "—" : dto.PaEmissionId!,
        Detail = string.IsNullOrWhiteSpace(dto.Detail) ? "—" : dto.Detail!,
        PaResponseLines = PaResponseSnapshotFormatter.Format(dto.PaResponseSnapshot),
        LastActivityUtc = dto.LastActivityUtc,
        Documents = dto.Documents
            .Select(d => new B2cMarginEmissionDetailDocumentRow
            {
                DocumentId = d.DocumentId,
                SourceReference = d.SourceReference,
                Family = DocumentFamilyDisplay.For(d.SourceReference) ?? "—",
            })
            .ToList(),
    };
}
