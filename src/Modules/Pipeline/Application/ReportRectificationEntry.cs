namespace Liakont.Modules.Pipeline.Application;

using System;
using Liakont.Modules.Pipeline.Domain.Rectification;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Entrée IMMUABLE du journal des rectificatifs d'e-reporting (<c>pipeline.report_rectifications</c>, PIP04,
/// flux RE). Chaque tentative de rectification d'une période (initiale puis chaque rectificatif) ajoute une
/// entrée ; l'historique complet est conservé APPEND-ONLY (l'ancien état n'est jamais effacé — F07-F08 §B.1,
/// CLAUDE.md n°4). DISTINCT de la projection d'agrégation (recalculée) et de
/// <c>payments.payment_aggregate_events</c> (audit de transmission de PIP03b).
/// </summary>
public sealed record ReportRectificationEntry
{
    /// <summary>Identifiant de l'entrée.</summary>
    public required Guid Id { get; init; }

    /// <summary>Type de flux de l'e-reporting (domestique / international).</summary>
    public required PaymentReportFlux Flux { get; init; }

    /// <summary>Premier jour de la période rectifiée (inclus).</summary>
    public required DateOnly PeriodStart { get; init; }

    /// <summary>Dernier jour de la période rectifiée (inclus).</summary>
    public required DateOnly PeriodEnd { get; init; }

    /// <summary>Empreinte du contenu rectifié (clé d'idempotence — hex SHA-256 minuscule).</summary>
    public required string ContentHash { get; init; }

    /// <summary>Issue de la tentative (transmis / en attente de capacité / rejeté / erreur technique).</summary>
    public required ReportRectificationStatus Status { get; init; }

    /// <summary>Identifiant attribué par la Plateforme Agréée (transmission acceptée), ou <c>null</c>.</summary>
    public string? PaReportId { get; init; }

    /// <summary>Snapshot JSON des lignes rectifiées transmises (preuve d'audit), ou <c>null</c> si aucune transmission.</summary>
    public string? PayloadSnapshot { get; init; }

    /// <summary>Réponse brute de la Plateforme Agréée (peut être non-JSON), ou <c>null</c>.</summary>
    public string? PaResponseSnapshot { get; init; }

    /// <summary>Message opérateur / motif (français), ou <c>null</c>.</summary>
    public string? Detail { get; init; }

    /// <summary>Horodatage de l'entrée (UTC).</summary>
    public required DateTimeOffset CreatedUtc { get; init; }
}
