namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Contracts.Classification;

/// <summary>
/// VAL04 — couvre le TROU de classification de l'avoir (F04 §3.5bis, finding RD4-16) : un document dont
/// la SOURCE porte la nature « avoir » UNIQUEMENT par son type brut
/// (<see cref="Liakont.Agent.Contracts.Pivot.PivotDocumentDto.SourceDocumentKind"/>), SANS aucune
/// référence d'origine (<c>CreditNoteRefs</c> vide). <see cref="CreditNoteRule"/> ne le détecte pas (il
/// part du signal STRUCTUREL EN 16931 BG-3) ; un tel avoir serait alors traité comme une facture
/// (signe/sens inversé, envoi faux). Cette règle le RECONNAÎT via la table de correspondance TENANT
/// (<see cref="ISourceDocumentKindClassifier"/>) et le BLOQUE : son origine n'est pas résoluble, on ne
/// fabrique jamais de référence (F07-F08 §B.4, CLAUDE.md n°2/n°3).
/// </summary>
/// <remarks>
/// <para><b>Frontière.</b> Dépend de l'abstraction <see cref="ISourceDocumentKindClassifier"/> (Contracts,
/// tenant-scopée par <see cref="DocumentValidationContext.CompanyId"/>), jamais d'un module concret
/// (module-rules.md §3). Détection seule, aucune écriture.</para>
/// <para><b>Pas de double blocage / pas d'invention.</b> La règle ne produit une anomalie QUE pour un
/// document classé <see cref="SourceDocumentClassification.CreditNote"/> ET sans aucune référence
/// d'origine. Avec une référence, les contrôles d'avoir nominaux (<see cref="CreditNoteRule"/> : orphelin,
/// original non émis, montants positifs) prennent le relais — cette règle reste silencieuse. Un type
/// source classé <see cref="SourceDocumentClassification.Invoice"/> ou non cartographié
/// (<see cref="SourceDocumentClassification.Unmapped"/>) ne produit RIEN : on ne devine pas.</para>
/// </remarks>
public sealed class SourceDocumentKindCreditNoteRule : IDocumentRule
{
    /// <summary>
    /// Code d'anomalie : document classé « avoir » par son type source mais SANS aucune référence
    /// d'origine résoluble (origine non fabriquée → bloqué).
    /// </summary>
    public const string CreditNoteKindWithoutOriginCode = "CREDIT_NOTE_KIND_WITHOUT_ORIGIN";

    private readonly ISourceDocumentKindClassifier _classifier;

    /// <summary>Crée la règle de classification du type source en avoir.</summary>
    /// <param name="classifier">Classificateur tenant-scopé du type source (table de correspondance).</param>
    public SourceDocumentKindCreditNoteRule(ISourceDocumentKindClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    /// <inheritdoc />
    public string Code => "SOURCE_DOCUMENT_KIND_CREDIT_NOTE";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;

        // Un document qui porte déjà une référence d'origine relève des contrôles nominaux de l'avoir
        // (CreditNoteRule). Cette règle ne traite QUE le cas où la nature avoir n'est connue que du type
        // source. Court-circuit avant l'appel au classificateur (rien à reclasser).
        if (document.CreditNoteRefs.Count > 0)
        {
            return Array.Empty<ValidationIssue>();
        }

        var classification = await _classifier
            .ClassifyAsync(context.CompanyId, document.SourceDocumentKind, cancellationToken)
            .ConfigureAwait(false);

        if (classification != SourceDocumentClassification.CreditNote)
        {
            // Facture ou non cartographié : aucune invention. Le repli structurel (aucune référence ici)
            // laisse le document suivre son cours de facture.
            return Array.Empty<ValidationIssue>();
        }

        var message =
            $"Le document n° {document.Number} est classé « avoir » d'après son type source " +
            $"« {document.SourceDocumentKind} », mais il ne référence aucune facture d'origine. Un avoir doit " +
            "préciser la facture qu'il rectifie (numéro + date). Rattachez l'origine ou traitez ce document " +
            "manuellement ; il reste bloqué (aucune référence n'est fabriquée).";
        var detail =
            $"Type source « {document.SourceDocumentKind} » classé avoir (table tenant) ; aucune référence d'origine (CreditNoteRefs vide).";

        return new[] { ValidationIssue.Blocking(CreditNoteKindWithoutOriginCode, message, detail, fieldRef: "BT-3") };
    }
}
