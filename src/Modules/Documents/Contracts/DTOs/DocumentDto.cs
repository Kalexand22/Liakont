namespace Liakont.Modules.Documents.Contracts.DTOs;

using System;

/// <summary>
/// Vue complète d'un document pour l'API/console (item TRK01). Montants en <see cref="decimal"/>
/// (CLAUDE.md n°1) ; l'état et le type sont exposés en chaîne (lisibilité ; pas de fuite de
/// l'énumération du Domain hors du module).
/// </summary>
public sealed record DocumentDto
{
    public required Guid Id { get; init; }

    public required string SourceReference { get; init; }

    public required string DocumentNumber { get; init; }

    public required string DocumentType { get; init; }

    public required DateOnly IssueDate { get; init; }

    public string? SupplierSiren { get; init; }

    public string? CustomerName { get; init; }

    public required bool CustomerIsCompanyHint { get; init; }

    public required decimal TotalNet { get; init; }

    public required decimal TotalTax { get; init; }

    public required decimal TotalGross { get; init; }

    public required string State { get; init; }

    public required string PayloadHash { get; init; }

    public string? PaDocumentId { get; init; }

    public string? MappingVersion { get; init; }

    public required DateTimeOffset FirstSeenUtc { get; init; }

    public required DateTimeOffset LastUpdateUtc { get; init; }
}
