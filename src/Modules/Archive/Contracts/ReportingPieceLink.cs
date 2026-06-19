namespace Liakont.Modules.Archive.Contracts;

using System;

/// <summary>
/// Lien IMMUABLE entre une transmission d'e-reporting B2C (déclaration 10.3, identifiée par
/// <see cref="DocumentId"/>) et une PIÈCE source (<see cref="SourceReference"/> du pivot, ADR-0007 —
/// p. ex. un bordereau acheteur). Figé à la transmission, append-only (CLAUDE.md n°4), tenant-scopé
/// (<see cref="CompanyId"/>). Persisté dans <c>documents.reporting_piece_links</c> (migration V011, item B2C03).
/// </summary>
/// <param name="LinkId">Identifiant du lien.</param>
/// <param name="CompanyId">Tenant propriétaire du lien (défense en profondeur, jamais cross-tenant — n°9).</param>
/// <param name="DocumentId">La déclaration 10.3 transmise (la « transmission »).</param>
/// <param name="SourceReference">La pièce source rattachée (référence de pièce du pivot).</param>
/// <param name="LinkedAtUtc">Horodatage du gel du lien (instant de la transmission).</param>
public sealed record ReportingPieceLink(
    Guid LinkId,
    Guid CompanyId,
    Guid DocumentId,
    string SourceReference,
    DateTimeOffset LinkedAtUtc);
