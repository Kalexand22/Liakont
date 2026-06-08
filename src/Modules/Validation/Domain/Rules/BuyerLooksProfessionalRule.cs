namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Detection;

/// <summary>
/// Garde-fou B2B/B2C (F08) : si l'acheteur d'un bordereau (traité en B2C par défaut) « semble être un
/// professionnel » (heuristique F07-F08 §A.4, <see cref="CompanyHintDetector"/>), le document est
/// BLOQUÉ. Déclarer en e-reporting B2C une vente qui relève de l'e-invoicing B2B prive l'acheteur pro
/// de sa facture déductible = manquement à l'obligation (F08, BOI-TVA-SECT-90-50). Le blocage est la
/// protection V1 : l'opérateur tranche ensuite (« confirmer B2C » ou « traiter manuellement ») via
/// l'endpoint verdict (API02) et la console (WEB03). La règle DÉTECTE, elle ne corrige ni ne reclasse
/// jamais le document. Anomalie BLOQUANTE (CLAUDE.md n°3). Règle pure : elle ne lit que le document.
/// </summary>
public sealed class BuyerLooksProfessionalRule : IDocumentRule
{
    /// <summary>L'acheteur présente des indices d'un professionnel : circuit B2B requis, non géré automatiquement en V1.</summary>
    public const string BuyerLooksProfessional = "BUYER_LOOKS_PROFESSIONAL";

    /// <inheritdoc/>
    public string Code => "BUYER_LOOKS_PROFESSIONAL";

    /// <inheritdoc/>
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;
        var buyer = document.Customer;
        var issues = new List<ValidationIssue>();

        if (buyer is null)
        {
            // B2C sans tiers identifié (Customer = null) : aucun acheteur à qualifier (cohérent avec BuyerIdentityRule).
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
        }

        if (context.BuyerConfirmedAsIndividual)
        {
            // Verdict OPÉRATEUR « confirmer particulier (B2C) » posé pour ce document (F08 §A.4 : « confirmer B2C
            // → débloque en B2C, décision journalisée »). La décision tranchée et tracée prime sur l'heuristique
            // d'indice : le garde-fou ne bloque plus CE document. Ce n'est pas un affaiblissement de la règle
            // (CLAUDE.md n°3) mais l'incorporation d'une entrée légitime — la règle reste détection-seule pour
            // tout document non tranché par l'opérateur.
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
        }

        var hint = CompanyHintDetector.Detect(buyer);
        if (hint.LooksProfessional)
        {
            issues.Add(ValidationIssue.Blocking(
                BuyerLooksProfessional,
                $"L'acheteur \"{buyer.Name}\" du document n° {document.Number} semble être un professionnel. Une facture électronique B2B est requise (non gérée automatiquement en V1). Traitez ce bordereau manuellement ou confirmez qu'il s'agit d'un particulier.",
                $"Garde-fou B2B/B2C (F07-F08 §A.4) : indice société source={hint.HasCompanyHintField}, n° TVA présent={hint.HasVatNumber}, forme juridique={hint.MatchedLegalForm ?? "—"}.",
                "BT-44"));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
