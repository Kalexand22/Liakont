namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Contracts.CreditNotes;

/// <summary>
/// VAL04 — contrôles propres aux AVOIRS (F04 §3.5, F07-F08). Un avoir n'est transmissible que si :
/// <list type="number">
/// <item>il porte une référence à sa facture d'origine (EN 16931 BG-3 / BT-25) — la référence est
/// PRÉSENTE et non vide ;</item>
/// <item>cette facture d'origine est CONNUE de la plateforme ET DÉJÀ ÉMISE (sinon avoir orphelin —
/// jamais de référence fabriquée, F07-F08 §B.4) ;</item>
/// <item>tous ses montants sont POSITIFS : la fonction « crédit » est portée par le TYPE de document
/// (CreditNote 381), jamais par le signe — mélanger 381 + montants négatifs est prohibé (EN 16931,
/// F07-F08 §B.2).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Détection d'un avoir.</b> Cette règle traite un document comme un avoir lorsqu'il porte au
/// moins une référence de document d'origine (<see cref="PivotDocumentDto.CreditNoteRefs"/>, le signal
/// structurel EN 16931 BG-3). La classification générale facture/avoir à partir du type source brut
/// (<see cref="PivotDocumentDto.SourceDocumentKind"/>) est un concern PLATEFORME distinct, non encore
/// bâti et NON spécifié (la correspondance type-source → avoir varie par logiciel) : VAL04 ne l'invente
/// pas (CLAUDE.md n°2). Conséquence assumée : un avoir émis SANS aucune référence d'origine relève de ce
/// classificateur à venir ; ici, le cas orphelin couvert est l'avoir qui RÉFÉRENCE un original absent de
/// la plateforme (cas réel : avoir EncheresV6 dont l'original est pré-réforme / hors passerelle).</para>
/// <para><b>Frontière.</b> La règle dépend de l'abstraction <see cref="IIssuedInvoiceLookup"/>
/// (Contracts), jamais du module Documents directement (module-rules.md §3). La recherche est
/// tenant-scopée par <see cref="DocumentValidationContext.CompanyId"/> (CLAUDE.md n°9). Détection seule,
/// aucune écriture.</para>
/// </remarks>
public sealed class CreditNoteRule : IDocumentRule
{
    /// <summary>Code d'anomalie : référence à la facture d'origine manquante/vide sur un avoir.</summary>
    public const string ReferenceMissingCode = "CREDIT_NOTE_REF_MISSING";

    /// <summary>Code d'anomalie : avoir orphelin (facture d'origine inconnue de la plateforme).</summary>
    public const string OrphanCode = "CREDIT_NOTE_ORPHAN";

    /// <summary>Code d'anomalie : facture d'origine connue mais pas encore émise (émettre l'original d'abord).</summary>
    public const string OriginalNotIssuedCode = "CREDIT_NOTE_ORIGINAL_NOT_ISSUED";

    /// <summary>Code d'anomalie : avoir portant un ou des montants négatifs (prohibé EN 16931).</summary>
    public const string NegativeAmountCode = "CREDIT_NOTE_NEGATIVE_AMOUNT";

    private readonly IIssuedInvoiceLookup _originalInvoiceLookup;

    /// <summary>Crée la règle des avoirs.</summary>
    /// <param name="originalInvoiceLookup">Recherche de l'état de la facture d'origine (tenant-scopée).</param>
    public CreditNoteRule(IIssuedInvoiceLookup originalInvoiceLookup)
    {
        _originalInvoiceLookup = originalInvoiceLookup ?? throw new ArgumentNullException(nameof(originalInvoiceLookup));
    }

    /// <inheritdoc />
    public string Code => "CREDIT_NOTE";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;

        // Détection : sans référence d'origine, ce document n'est pas traité comme un avoir par cette règle
        // (voir la note de frontière dans la documentation de la classe).
        if (document.CreditNoteRefs.Count == 0)
        {
            return Array.Empty<ValidationIssue>();
        }

        var issues = new List<ValidationIssue>();

        AddNegativeAmountIssueIfAny(document, issues);

        foreach (var reference in document.CreditNoteRefs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(reference.Number))
            {
                var refMissingMessage =
                    $"L'avoir n° {document.Number} ne précise pas le numéro de la facture d'origine qu'il rectifie. " +
                    "Renseignez la facture d'origine (numéro + date) ou traitez ce document manuellement ; il reste " +
                    "bloqué tant que l'origine n'est pas identifiée.";
                var refMissingDetail = $"Référence d'origine sans numéro (date : {reference.IssueDate:yyyy-MM-dd}).";
                issues.Add(ValidationIssue.Blocking(ReferenceMissingCode, refMissingMessage, refMissingDetail, fieldRef: "BT-25"));
                continue;
            }

            var status = await _originalInvoiceLookup.FindOriginalStatusAsync(context.CompanyId, reference, cancellationToken)
                .ConfigureAwait(false);

            switch (status)
            {
                case OriginalInvoiceStatus.KnownIssued:
                    break;

                case OriginalInvoiceStatus.KnownNotIssued:
                    var notIssuedMessage =
                        $"L'avoir n° {document.Number} rectifie la facture n° {reference.Number} qui n'a pas encore été " +
                        "émise par la passerelle. Émettez d'abord la facture d'origine, puis renvoyez l'avoir.";
                    var notIssuedDetail = $"Facture d'origine connue mais non émise : n° {reference.Number} du {reference.IssueDate:yyyy-MM-dd}.";
                    issues.Add(ValidationIssue.Blocking(OriginalNotIssuedCode, notIssuedMessage, notIssuedDetail, fieldRef: "BT-25"));
                    break;

                case OriginalInvoiceStatus.Unknown:
                default:
                    var orphanMessage =
                        $"L'avoir n° {document.Number} rectifie la facture n° {reference.Number} qui est inconnue de la " +
                        "passerelle (facture d'origine émise hors passerelle ou non rattachée). Vérifiez le rattachement " +
                        "ou traitez cet avoir manuellement ; il reste bloqué (aucune référence n'est fabriquée).";
                    var orphanDetail = $"Facture d'origine inconnue : n° {reference.Number} du {reference.IssueDate:yyyy-MM-dd}.";
                    issues.Add(ValidationIssue.Blocking(OrphanCode, orphanMessage, orphanDetail, fieldRef: "BT-25"));
                    break;
            }
        }

        return issues;
    }

    private static void AddNegativeAmountIssueIfAny(PivotDocumentDto document, List<ValidationIssue> issues)
    {
        var negatives = new List<string>();

        if (document.Totals.TotalNet < 0m)
        {
            negatives.Add($"total HT {document.Totals.TotalNet}");
        }

        if (document.Totals.TotalTax < 0m)
        {
            negatives.Add($"total TVA {document.Totals.TotalTax}");
        }

        if (document.Totals.TotalGross < 0m)
        {
            negatives.Add($"total TTC {document.Totals.TotalGross}");
        }

        if (document.PrepaidAmount is { } prepaid && prepaid < 0m)
        {
            negatives.Add($"acompte {prepaid}");
        }

        for (var lineIndex = 0; lineIndex < document.Lines.Count; lineIndex++)
        {
            var line = document.Lines[lineIndex];
            if (line.NetAmount < 0m)
            {
                negatives.Add($"ligne #{lineIndex + 1} HT {line.NetAmount}");
            }

            foreach (var tax in line.Taxes)
            {
                if (tax.TaxAmount < 0m)
                {
                    negatives.Add($"ligne #{lineIndex + 1} TVA {tax.TaxAmount}");
                }
            }
        }

        if (negatives.Count == 0)
        {
            return;
        }

        var negativeMessage =
            $"L'avoir n° {document.Number} porte un ou plusieurs montants négatifs. Sur un avoir, tous les montants " +
            "doivent être POSITIFS : la nature « avoir » est portée par le type de document, jamais par le signe. " +
            "Corrigez l'extraction (montants en positif) ; le document reste bloqué.";
        var negativeDetail = "Montants négatifs détectés : " + string.Join(" ; ", negatives) + ".";
        issues.Add(ValidationIssue.Blocking(NegativeAmountCode, negativeMessage, negativeDetail, fieldRef: "BT-131/BT-110"));
    }
}
