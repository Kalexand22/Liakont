namespace Liakont.Modules.Pipeline.Domain.Payments;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Cœur PUR (sans I/O) de l'agrégation de paiement (PIP03a, F09 §2). Ventile chaque encaissement RÉSOLU par
/// taux selon la ventilation SOURCÉE de son document (snapshot ADR-0015), agrège par jour×taux, et qualifie
/// les agrégats selon le paramétrage fiscal du tenant et la capacité de la Plateforme Agréée.
/// </summary>
/// <remarks>
/// <para>AUCUNE règle fiscale inventée (CLAUDE.md n°2/3) : seuls les documents MONO-CATÉGORIE
/// <see cref="OperationCategory.PrestationServices"/> sont agrégés (leur ventilation par taux est
/// directement utilisable). Un document <see cref="OperationCategory.Mixte"/> est SUSPENDU (découpage
/// frais/adjudication non sourcé, D-b — réservé à PIP03b). Une <see cref="OperationCategory.LivraisonBiens"/>
/// n'est pas concernée (exigibilité à la livraison). La ventilation d'un encaissement reprend la PROPORTION
/// par taux du document (F09 §2 : « montant encaissé ventilé par taux ») — jamais un lettrage ligne à ligne
/// inventé. Arrondi commercial half-up à 2 décimales (<see cref="PivotRounding.RoundAmount"/>, CLAUDE.md n°1).</para>
/// <para>AUCUN fenêtrage de période, AUCUN envoi : ce sont des concerns de PIP03b (D-a). Tout agrégat reste à
/// l'état calculé + une qualification (transmissible / suspendu / non requis / capacité en attente).</para>
/// </remarks>
public static class PaymentAggregationCalculator
{
    /// <summary>Motif : méthode d'imputation des frais non renseignée (F09 §5.2 — jamais de prorata par défaut).</summary>
    public const string FeeMethodMissingReason =
        "Méthode d'imputation des frais non renseignée — consultez votre expert-comptable (Paramétrage › Fiscal). " +
        "Aucune méthode n'est appliquée par défaut ; l'e-reporting de paiement reste suspendu.";

    /// <summary>Motif : une décision fiscale est en attente (TVA sur les débits / catégorie d'opération / fréquence déclarative).</summary>
    public const string FiscalPendingReason =
        "Décision fiscale en attente (TVA sur les débits / catégorie d'opération / fréquence déclarative) — " +
        "consultez votre expert-comptable (Paramétrage › Fiscal). L'e-reporting de paiement reste suspendu.";

    /// <summary>Motif : TVA sur les débits (exigibilité à la facturation — pas d'e-reporting de paiement).</summary>
    public const string VatOnDebitsNotRequiredReason =
        "Non requis — option TVA sur les débits : l'exigibilité est à la facturation, pas à l'encaissement. " +
        "Les agrégats sont calculés pour la traçabilité mais ne sont pas transmis.";

    /// <summary>Motif : la Plateforme Agréée ne déclare pas encore la capacité de transmission des paiements.</summary>
    public const string PendingCapabilityReason =
        "En attente : la Plateforme Agréée ne déclare pas encore la capacité de transmission des paiements " +
        "(Flux 10.4). Les agrégats partiront automatiquement dès l'activation de la capacité, sans autre changement.";

    /// <summary>Motif : document Mixte (découpage frais/adjudication non sourcé — suspendu).</summary>
    public const string MixteSuspendedReason =
        "Document Mixte (biens + services) : le découpage de la part frais soumise à l'e-reporting de paiement " +
        "n'est pas déterminé par une règle sourcée — suspendu (sera traité ultérieurement). Aucune part devinée.";

    /// <summary>Motif : livraison de biens (exigibilité à la livraison — non concernée par l'e-reporting de paiement).</summary>
    public const string GoodsNotApplicableReason =
        "Livraison de biens : exigibilité de la TVA à la livraison — non concernée par l'e-reporting de paiement.";

    /// <summary>Motif : autoliquidation (reverse charge) — exclue de l'e-reporting de paiement (F09 §2).</summary>
    public const string ReverseChargeReason =
        "Autoliquidation (reverse charge, catégorie AE) : la TVA n'est pas collectée par le fournisseur — exclue " +
        "de l'e-reporting de paiement (F09 §2). Un document mêlant autoliquidation et taux collectés est suspendu " +
        "(la part à reporter n'est pas isolable par une règle sourcée).";

    /// <summary>Motif : taux de TVA non résolu dans la ventilation — impossible de ventiler l'encaissement par taux.</summary>
    public const string UnresolvedRateReason =
        "Taux de TVA non résolu dans la ventilation du document : impossible de ventiler l'encaissement par taux. " +
        "Action opérateur : vérifiez la table de mapping TVA et la donnée source de ce document.";

    /// <summary>Motif : total document nul — impossible de calculer la couverture de l'encaissement.</summary>
    public const string ZeroDocumentTotalReason =
        "Total du document nul : impossible de répartir l'encaissement par taux. Action opérateur : vérifiez la " +
        "donnée source de ce document.";

    /// <summary>Nom UNCL5305 de l'autoliquidation (reverse charge) — <see cref="VatCategory.AE"/>.</summary>
    private const string ReverseChargeCategory = nameof(VatCategory.AE);

    /// <summary>
    /// Agrège les encaissements résolus par jour×taux et qualifie les agrégats. Le contexte fiscal et la
    /// capacité PA déterminent la qualification UNIFORME des agrégats calculés (suspension/non requis/etc.) ;
    /// les agrégats sont TOUJOURS calculés pour la traçabilité (F09 amendement, CLAUDE.md). Sortie déterministe
    /// (triée par jour puis taux).
    /// </summary>
    public static PaymentAggregationResult Aggregate(
        IReadOnlyList<ResolvedPayment> payments,
        PaymentFiscalContext fiscal,
        bool paSupportsDomesticPaymentReporting)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(fiscal);

        var (status, reason) = QualifyTenant(fiscal, paSupportsDomesticPaymentReporting);

        var exclusions = new List<PaymentExclusion>();
        var orderedKeys = new List<(DateOnly Date, decimal Rate)>();
        var baseByKey = new Dictionary<(DateOnly, decimal), decimal>();
        var vatByKey = new Dictionary<(DateOnly, decimal), decimal>();

        foreach (var payment in payments)
        {
            var document = payment.Document;
            switch (document.OperationCategory)
            {
                case OperationCategory.Mixte:
                    exclusions.Add(Exclude(payment, PaymentExclusionReason.MixteSuspended, MixteSuspendedReason));
                    continue;
                case OperationCategory.LivraisonBiens:
                    exclusions.Add(Exclude(payment, PaymentExclusionReason.GoodsNotApplicable, GoodsNotApplicableReason));
                    continue;
                case OperationCategory.PrestationServices:
                    break;
                default:
                    exclusions.Add(Exclude(payment, PaymentExclusionReason.GoodsNotApplicable, GoodsNotApplicableReason));
                    continue;
            }

            // Autoliquidation (AE, F09 §2) : la TVA n'est pas collectée → exclue. Un document mêlant AE et taux
            // collectés est SUSPENDU (la part reportable n'est pas isolable sans règle sourcée — même famille que
            // Mixte). Aucune part devinée (CLAUDE.md n°2).
            if (document.Lines.Any(line => string.Equals(line.Category, ReverseChargeCategory, StringComparison.Ordinal)))
            {
                exclusions.Add(Exclude(payment, PaymentExclusionReason.ReverseCharge, ReverseChargeReason));
                continue;
            }

            if (document.Lines.Any(line => line.Rate is null))
            {
                exclusions.Add(Exclude(payment, PaymentExclusionReason.UnresolvedRate, UnresolvedRateReason));
                continue;
            }

            var total = document.Lines.Sum(line => line.TaxableBase + line.VatAmount);
            if (total == 0m)
            {
                exclusions.Add(Exclude(payment, PaymentExclusionReason.ZeroDocumentTotal, ZeroDocumentTotalReason));
                continue;
            }

            var coverage = payment.Amount / total;
            foreach (var line in document.Lines)
            {
                var rate = line.Rate!.Value;
                var key = (payment.Date, rate);
                if (!baseByKey.ContainsKey(key))
                {
                    orderedKeys.Add(key);
                    baseByKey[key] = 0m;
                    vatByKey[key] = 0m;
                }

                baseByKey[key] += PivotRounding.RoundAmount(line.TaxableBase * coverage);
                vatByKey[key] += PivotRounding.RoundAmount(line.VatAmount * coverage);
            }
        }

        var aggregates = orderedKeys
            .OrderBy(k => k.Date)
            .ThenBy(k => k.Rate)
            .Select(k => new PaymentDailyAggregate
            {
                Date = k.Date,
                Rate = k.Rate,
                TaxableBase = baseByKey[k],
                VatAmount = vatByKey[k],
                Status = status,
                Reason = reason,
            })
            .ToList();

        return new PaymentAggregationResult { Aggregates = aggregates, Exclusions = exclusions };
    }

    /// <summary>
    /// Qualifie UNIFORMÉMENT les agrégats du tenant. Ordre : TVA sur les débits (non requis) prime ; sinon une
    /// méthode d'imputation manquante OU un paramètre fiscal en attente suspend ; sinon l'absence de capacité PA
    /// met en attente ; sinon transmissible.
    /// </summary>
    private static (PaymentAggregationStatus Status, string? Reason) QualifyTenant(
        PaymentFiscalContext fiscal,
        bool paSupportsDomesticPaymentReporting)
    {
        if (fiscal.VatOnDebits == true)
        {
            return (PaymentAggregationStatus.NotRequired, VatOnDebitsNotRequiredReason);
        }

        if (!fiscal.HasFeeImputationMethod)
        {
            return (PaymentAggregationStatus.Suspended, FeeMethodMissingReason);
        }

        if (fiscal.VatOnDebits is null || !fiscal.HasOperationCategory || !fiscal.HasReportingFrequency)
        {
            return (PaymentAggregationStatus.Suspended, FiscalPendingReason);
        }

        if (!paSupportsDomesticPaymentReporting)
        {
            return (PaymentAggregationStatus.PendingCapability, PendingCapabilityReason);
        }

        return (PaymentAggregationStatus.Calculated, null);
    }

    private static PaymentExclusion Exclude(ResolvedPayment payment, PaymentExclusionReason reason, string detail) =>
        new()
        {
            RelatedDocumentNumber = payment.RelatedDocumentNumber,
            Reason = reason,
            Detail = detail,
        };
}
