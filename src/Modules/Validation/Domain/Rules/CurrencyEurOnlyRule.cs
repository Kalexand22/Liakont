namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Verrou EUR-only (RD408, finding RD4-03). L'intégralité des cibles produit est en FULL EURO
/// (décision Karl, 2026-06-19, ADR-0004 D4 Famille 1) : Liakont V1 ne gère PAS le multi-devises
/// (pas de taux de change BT-6, pas de TVA exprimée en EUR BT-111). Une devise étrangère serait
/// recopiée brute vers la Plateforme Agréée et dans le CII sans équivalent EUR — donnée fiscale
/// fausse. Conformément à CLAUDE.md n°3 (« bloquer plutôt qu'envoyer faux »), on BLOQUE tout
/// document libellé dans une devise autre que l'euro plutôt que de transmettre un montant non
/// converti.
/// <para>
/// Frontière avec <see cref="StructureRule"/> : la validité ISO 4217 (BT-5) reste contrôlée par
/// <see cref="StructureRule"/> (code <see cref="StructureRule.CurrencyInvalidCode"/>). Cette règle
/// ne se déclenche QUE pour une devise ISO 4217 VALIDE mais distincte de l'euro (USD, GBP…) : un
/// code absent/invalide est déjà bloqué par <see cref="StructureRule"/>, on évite ainsi un double
/// signal bloquant sur le même champ. La comparaison à « EUR » est insensible à la casse, comme le
/// référentiel ISO 4217 (les codes sont définis en majuscules ; on tolère la casse d'entrée).
/// </para>
/// Règle PURE : ne lit que le document, aucune dépendance externe. AUCUNE logique multi-devises
/// n'est ajoutée (NON-OBJECTIF explicite de RD408 : ni ExchangeRate BT-6, ni TaxCurrencyCode,
/// ni TVA-en-EUR BT-111).
/// </summary>
public sealed class CurrencyEurOnlyRule : IDocumentRule
{
    /// <summary>Devise étrangère (ISO 4217 valide ≠ EUR) — non supportée en V1.</summary>
    public const string NonEurCurrencyCode = "DOC_CURRENCY_NOT_EUR";

    /// <summary>Code devise unique supporté en V1 (EUR-only). ISO 4217 BT-5.</summary>
    private const string EuroCode = "EUR";

    /// <inheritdoc/>
    public string Code => "CURRENCY_EUR_ONLY";

    /// <inheritdoc/>
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;
        var issues = new List<ValidationIssue>();

        // Ne se déclenche que sur une devise ISO 4217 VALIDE mais ≠ EUR : un code absent/invalide est
        // déjà pris en charge par StructureRule (DOC_CURRENCY_INVALID) — pas de double blocage.
        if (Iso4217Currencies.IsValid(document.CurrencyCode)
            && !string.Equals(document.CurrencyCode, EuroCode, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(ValidationIssue.Blocking(
                NonEurCurrencyCode,
                $"La devise « {document.CurrencyCode} » du document n° {document.Number} n'est pas l'euro. Liakont ne prend en charge que les documents en euros (EUR) dans cette version : convertissez le document en euros dans le logiciel source avant l'envoi. Le document reste bloqué.",
                $"CurrencyCode = '{document.CurrencyCode}' ≠ EUR (verrou EUR-only V1, RD408 ; aucune conversion multi-devises n'est effectuée).",
                "BT-5"));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
