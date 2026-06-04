namespace Liakont.Modules.Validation.Domain.Detection;

using System.Text.RegularExpressions;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Garde-fou B2B/B2C (F08) : heuristique de détection d'un acheteur professionnel quand le SIREN
/// acheteur est absent (F07-F08 §A.4). Toute l'heuristique vit ICI, sur la plateforme — l'agent ne
/// fait que transcrire des champs source bruts, sans aucune décision (frontière agent/plateforme,
/// amendement F01-F02 du 2026-06-03 ; CLAUDE.md n°6). Les indices et la liste de formes juridiques
/// sont EXACTEMENT ceux de F07-F08 §A.4 : aucun indice ajouté, aucun seuil inventé (CLAUDE.md n°2).
/// Le montant n'est JAMAIS un critère : il n'est même pas accessible ici (on ne reçoit que l'acheteur).
/// </summary>
public static partial class CompanyHintDetector
{
    /// <summary>
    /// Évalue les indices « professionnel » de F07-F08 §A.4 sur l'acheteur. Indices FORTS : indice
    /// « société » brut de la source (champ <c>societe</c>) ; n° de TVA intracommunautaire présent.
    /// Indice MOYEN : forme juridique repérée dans la raison sociale. Le verdict est positif dès
    /// qu'un indice fort OU l'indice de forme juridique se déclenche.
    /// </summary>
    /// <param name="buyer">L'acheteur (destinataire) du document. Obligatoire.</param>
    /// <returns>Le détail des indices déclenchés et le verdict <see cref="CompanyHintResult.LooksProfessional"/>.</returns>
    public static CompanyHintResult Detect(PivotPartyDto buyer)
    {
        ArgumentNullException.ThrowIfNull(buyer);

        // Indice FORT : indice « société » brut porté par la source (champ societe non vide), transcrit
        // tel quel par l'agent (PivotPartyDto.IsCompanyHint) — aucune heuristique côté agent.
        var hasCompanyHintField = buyer.IsCompanyHint;

        // Indice FORT : n° de TVA intracommunautaire PRÉSENT (F07-F08 §A.4 dit « présent », pas
        // « valide ») — un n° étranger ou non encore vérifié signale tout autant un professionnel ;
        // filtrer sur la validité du format FR sous-détecterait et affaiblirait le garde-fou.
        var hasVatNumber = !string.IsNullOrWhiteSpace(buyer.VatNumber);

        // Indice MOYEN : forme juridique repérée dans la raison sociale (regex, liste exacte de la spec).
        var match = LegalFormPattern().Match(buyer.Name);
        var matchedLegalForm = match.Success ? match.Value : null;

        return new CompanyHintResult(hasCompanyHintField, hasVatNumber, matchedLegalForm);
    }

    /// <summary>
    /// Formes juridiques de F07-F08 §A.4 (« SARL, SAS, SA, EURL, EI »), repérées comme tokens entiers
    /// (limites de mot <c>\b</c>) pour éviter les faux positifs en sous-chaîne (« EI » dans « BEIGNET »,
    /// « SA » dans « SABATIER »). La liste n'est PAS extensible sans amendement de la spec (acceptance
    /// VAL05) : le « … » de la spec est volontairement traité comme illustratif, ajouter une forme
    /// serait inventer une règle (CLAUDE.md n°2). Insensible à la casse (les sources écrivent la forme
    /// en majuscules ou non), invariante de culture pour un comportement déterministe.
    /// </summary>
    [GeneratedRegex(@"\b(?:SARL|SAS|SA|EURL|EI)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegalFormPattern();
}
