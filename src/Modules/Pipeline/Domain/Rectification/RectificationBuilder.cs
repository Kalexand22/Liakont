namespace Liakont.Modules.Pipeline.Domain.Rectification;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Liakont.Modules.Pipeline.Domain.Payments;

/// <summary>
/// Reconstruit l'agrégat rectifié COMPLET d'une période (PIP04, flux RE — F07-F08 §B.1) à partir de la
/// projection d'agrégation jour×taux de PIP03a. Pur (aucune E/S), déterministe, montants en
/// <see cref="decimal"/> (CLAUDE.md n°1). Le rebuild « annule et remplace » : il prend TOUTES les lignes
/// REPORTABLES (statut <see cref="PaymentAggregationStatus.Calculated"/>) dont le jour tombe dans les bornes
/// de la période, JAMAIS un delta. Il ne RECALCULE rien (aucune règle fiscale inventée, CLAUDE.md n°2) :
/// chaque ligne reprend fidèlement base/TVA/taux de la projection. Le fenêtrage (quelles bornes pour quelle
/// période déclarative) relève de PIP03b (cadence D-a non tranchée) — ici les bornes sont une ENTRÉE.
/// </summary>
public static class RectificationBuilder
{
    // Format décimal CANONIQUE pour l'empreinte : supprime les zéros de fin (20.00 == 20.0 == 20 → "20") tout
    // en préservant toute la précision significative. Déterministe et sans float (CLAUDE.md n°1) : deux jeux
    // d'agrégats LOGIQUEMENT identiques produisent la MÊME empreinte quelle que soit l'échelle décimale lue.
    private const string CanonicalDecimalFormat = "0.############################";

    /// <summary>
    /// Construit l'agrégat rectifié de la période <paramref name="periodStart"/>..<paramref name="periodEnd"/>
    /// (bornes incluses) à partir des agrégats jour×taux <paramref name="aggregates"/> (projection PIP03a).
    /// Seules les lignes REPORTABLES (Calculated) des bornes sont retenues, triées (jour puis taux) ; les
    /// lignes suspendues / non requises / en attente de capacité ne font pas partie de la déclaration.
    /// </summary>
    /// <param name="periodStart">Premier jour inclus de la période déclarative.</param>
    /// <param name="periodEnd">Dernier jour inclus de la période déclarative.</param>
    /// <param name="aggregates">Agrégats jour×taux courants (corrigés) du tenant — projection PIP03a.</param>
    /// <returns>L'agrégat rectifié complet de la période + son empreinte d'idempotence.</returns>
    public static ReportRectification Build(
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<PaymentDailyAggregate> aggregates)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        if (periodEnd < periodStart)
        {
            throw new ArgumentException(
                "La fin de la période rectifiée ne peut pas précéder son début.", nameof(periodEnd));
        }

        var lines = aggregates
            .Where(a => a.Status == PaymentAggregationStatus.Calculated)
            .Where(a => a.Date >= periodStart && a.Date <= periodEnd)
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Rate)
            .Select(a => new RectificationLine
            {
                Date = a.Date,
                Rate = a.Rate,
                TaxableBase = a.TaxableBase,
                VatAmount = a.VatAmount,
            })
            .ToList();

        return new ReportRectification
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Lines = lines,
            ContentHash = ComputeContentHash(periodStart, periodEnd, lines),
        };
    }

    private static string ComputeContentHash(DateOnly periodStart, DateOnly periodEnd, IReadOnlyList<RectificationLine> lines)
    {
        var canonical = new StringBuilder();
        canonical.Append(periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        canonical.Append('|');
        canonical.Append(periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        foreach (var line in lines)
        {
            canonical.Append('\n');
            canonical.Append(line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            canonical.Append('|');
            canonical.Append(Canonical(line.Rate));
            canonical.Append('|');
            canonical.Append(Canonical(line.TaxableBase));
            canonical.Append('|');
            canonical.Append(Canonical(line.VatAmount));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexStringLower(bytes);
    }

    private static string Canonical(decimal value) => value.ToString(CanonicalDecimalFormat, CultureInfo.InvariantCulture);
}
