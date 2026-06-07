namespace Liakont.Modules.Pipeline.Application;

using System;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Clé d'identité d'une période d'e-reporting rectifiable (PIP04) : type de flux + bornes de période. Une
/// rectification « annule et remplace » par SIREN (le tenant, implicite — database-per-tenant) + période
/// (F07-F08 §B.1). Le flux distingue les familles d'e-reporting (10.4 domestique / 10.2 international).
/// </summary>
public sealed record RectificationPeriodKey
{
    /// <summary>Type de flux de l'e-reporting de paiement (domestique / international).</summary>
    public required PaymentReportFlux Flux { get; init; }

    /// <summary>Premier jour de la période (inclus).</summary>
    public required DateOnly PeriodStart { get; init; }

    /// <summary>Dernier jour de la période (inclus).</summary>
    public required DateOnly PeriodEnd { get; init; }
}
