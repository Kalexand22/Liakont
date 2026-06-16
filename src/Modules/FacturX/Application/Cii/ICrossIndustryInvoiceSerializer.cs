namespace Liakont.Modules.FacturX.Application.Cii;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Sérialise un document pivot EN 16931 vers le XML <c>factur-x.xml</c> (Cross Industry Invoice, profil
/// EN 16931 COMFORT), <b>code maison</b> — jamais via le <c>convert</c> d'une PA (blueprint §6 ; ADR-0023
/// §2 / INV-FX-4). La sortie est DÉTERMINISTE du pivot seul ; le sérialiseur ne consulte aucune
/// <c>PaCapabilities</c> et ne référence aucun plug-in PA.
/// <para>
/// Frontière recopie/dérivation (F16 §3, ADR-0023 INV-FX-2/7) : les valeurs qualitatives (catégorie
/// BT-151, taux BT-152, VATEX) et les montants de ligne sont <b>recopiés</b> du pivot ; les agrégats non
/// portés (BG-23 BT-116/117, BT-106, BT-115) sont <b>dérivés</b> par arithmétique normative en
/// <see cref="decimal"/> puis <b>réconciliés</b> avec les totaux portés (BR-CO-14/15/16). Tout BT
/// obligatoire ni porté ni dérivable, ou tout écart non réconciliable, lève
/// <see cref="Domain.FacturXGenerationException"/> (blocage tracé — jamais de CII tronqué ni de valeur
/// fabriquée, CLAUDE.md n°3).
/// </para>
/// </summary>
public interface ICrossIndustryInvoiceSerializer
{
    /// <summary>
    /// Produit le XML CII (<c>factur-x.xml</c>) du document pivot, encodé en UTF-8.
    /// </summary>
    /// <param name="pivot">Le document pivot EN 16931 à sérialiser.</param>
    /// <returns>Les octets UTF-8 du XML <c>CrossIndustryInvoice</c>.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="pivot"/> est <c>null</c>.</exception>
    /// <exception cref="Domain.FacturXGenerationException">
    /// Un BT obligatoire est absent et non dérivable, ou un agrégat dérivé ne se réconcilie pas avec les
    /// totaux portés (BR-CO-14/15/16) — blocage.
    /// </exception>
    byte[] Serialize(PivotDocumentDto pivot);
}
