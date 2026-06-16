namespace Liakont.Modules.FacturX.Tests.Unit.Cii;

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Liakont.Agent.Contracts;
using Liakont.Modules.FacturX.Domain.Cii;

/// <summary>
/// Contrôle les identités arithmétiques EN 16931 <c>BR-CO-*</c> SUR LE CII PRODUIT (lecture indépendante
/// du XML, pas des calculs internes du sérialiseur) — tier « assertions structurelles » de F16 §8, dont
/// BR-CO-15 fatale. Règles sourcées (F04 / F16 §3.3 / ADR-0023 §2, jamais inventées) :
/// <list type="bullet">
///   <item>BR-CO-10 : BT-106 (LineTotalAmount) = Σ BT-131 (totaux de ligne).</item>
///   <item>BR-CO-13 (sans BG-20/21) : BT-109 (TaxBasisTotalAmount) = BT-106.</item>
///   <item>BR-CO-14 : BT-110 (TaxTotalAmount) = Σ BT-117 (CalculatedAmount des ventilations BG-23).</item>
///   <item>BR-CO-15 : BT-112 (GrandTotalAmount) = BT-109 + BT-110.</item>
///   <item>BR-CO-16 : BT-115 (DuePayableAmount) = BT-112 − BT-113 (TotalPrepaidAmount).</item>
///   <item>BR-CO-17 : chaque BT-117 = arrondi(BT-116 × BT-119 / 100).</item>
/// </list>
/// Montants en <see cref="decimal"/>, comparaison à zéro tolérance après arrondi half-up
/// (<see cref="PivotRounding"/>) — modèle <c>ArithmeticRule</c>. N'EST PAS le Schematron CEN/TC 434
/// complet (qui validerait l'ensemble des BR-* et exigerait un processeur XSLT2 + les artefacts CEN ;
/// voir la note de périmètre du module) : c'est le sous-ensemble arithmétique BR-CO produit en propre.
/// </summary>
internal static class CiiBusinessRuleChecker
{
    private static readonly XNamespace Ram = CiiProfile.RamNamespace;

    /// <summary>Retourne les violations BR-CO du CII (liste vide = conforme aux identités contrôlées).</summary>
    public static IReadOnlyList<string> Check(byte[] xml)
    {
        var violations = new List<string>();
        var document = XDocument.Parse(Encoding.UTF8.GetString(xml));

        var settlement = document.Descendants(Ram + "ApplicableHeaderTradeSettlement").Single();
        var summation = settlement.Element(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation")!;

        var lineTotal = Amount(summation, "LineTotalAmount");
        var taxBasis = Amount(summation, "TaxBasisTotalAmount");
        var taxTotal = Amount(summation, "TaxTotalAmount");
        var grandTotal = Amount(summation, "GrandTotalAmount");
        var prepaid = OptionalAmount(summation, "TotalPrepaidAmount");
        var duePayable = Amount(summation, "DuePayableAmount");

        var lineSum = Round(document
            .Descendants(Ram + "SpecifiedTradeSettlementLineMonetarySummation")
            .Sum(s => Amount(s, "LineTotalAmount")));

        if (lineTotal != lineSum)
        {
            violations.Add($"BR-CO-10 : BT-106 ({lineTotal}) ≠ Σ BT-131 ({lineSum}).");
        }

        if (taxBasis != lineTotal)
        {
            violations.Add($"BR-CO-13 : BT-109 ({taxBasis}) ≠ BT-106 ({lineTotal}).");
        }

        var headerTaxes = settlement.Elements(Ram + "ApplicableTradeTax").ToList();
        var breakdownTax = Round(headerTaxes.Sum(t => Amount(t, "CalculatedAmount")));
        if (taxTotal != breakdownTax)
        {
            violations.Add($"BR-CO-14 : BT-110 ({taxTotal}) ≠ Σ BT-117 ({breakdownTax}).");
        }

        if (grandTotal != Round(taxBasis + taxTotal))
        {
            violations.Add($"BR-CO-15 : BT-112 ({grandTotal}) ≠ BT-109 + BT-110 ({Round(taxBasis + taxTotal)}).");
        }

        if (duePayable != Round(grandTotal - prepaid))
        {
            violations.Add($"BR-CO-16 : BT-115 ({duePayable}) ≠ BT-112 − BT-113 ({Round(grandTotal - prepaid)}).");
        }

        foreach (var tax in headerTaxes)
        {
            var basis = Amount(tax, "BasisAmount");
            var rate = Amount(tax, "RateApplicablePercent");
            var calculated = Amount(tax, "CalculatedAmount");
            var expected = Round(basis * rate / 100m);
            if (calculated != expected)
            {
                violations.Add(
                    $"BR-CO-17 : BT-117 ({calculated}) ≠ arrondi(BT-116 {basis} × BT-119 {rate} / 100) = {expected}.");
            }
        }

        return violations;
    }

    private static decimal Amount(XElement parent, string name) =>
        decimal.Parse(parent.Element(Ram + name)!.Value, CultureInfo.InvariantCulture);

    private static decimal OptionalAmount(XElement parent, string name)
    {
        var element = parent.Element(Ram + name);
        return element is null ? 0m : decimal.Parse(element.Value, CultureInfo.InvariantCulture);
    }

    private static decimal Round(decimal value) => PivotRounding.RoundAmount(value);
}
