namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Contrôles structurels du document (F04 §3.3) : au moins une ligne (BLOQUANT), date d'émission
/// plausible — dans le futur = BLOQUANT, antérieure à l'an 2000 = ALERTE (décision F04 #4, EN 16931
/// BT-2) — et devise ISO 4217 valide (BLOQUANT, BT-5). L'horloge est injectée (<see cref="TimeProvider"/>)
/// pour un comportement déterministe et testable. Les niveaux de sévérité reproduisent la spec : on ne
/// durcit ni n'affaiblit (CLAUDE.md n°2, n°3).
/// </summary>
public sealed class StructureRule : IDocumentRule
{
    /// <summary>Document sans aucune ligne.</summary>
    public const string NoLinesCode = "DOC_NO_LINES";

    /// <summary>Date d'émission dans le futur.</summary>
    public const string DateInFutureCode = "DOC_DATE_FUTURE";

    /// <summary>Date d'émission invraisemblablement ancienne (alerte).</summary>
    public const string DateTooOldCode = "DOC_DATE_TOO_OLD";

    /// <summary>Devise absente ou hors ISO 4217.</summary>
    public const string CurrencyInvalidCode = "DOC_CURRENCY_INVALID";

    /// <summary>Date plancher en deçà de laquelle une date d'émission est jugée invraisemblable (alerte).</summary>
    private static readonly DateTime MinPlausibleDate = new(2000, 1, 1);

    private readonly TimeProvider _timeProvider;

    /// <summary>Crée la règle structurelle.</summary>
    /// <param name="timeProvider">Source de temps (pour la détection « date dans le futur »).</param>
    public StructureRule(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Code => "STRUCTURE";

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;
        var issues = new List<ValidationIssue>();

        if (document.Lines.Count == 0)
        {
            issues.Add(ValidationIssue.Blocking(
                NoLinesCode,
                $"Le document n° {document.Number} ne comporte aucune ligne. Un document sans ligne ne peut pas être transmis.",
                "Document.Lines est vide."));
        }

        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        if (document.IssueDate.Date > today)
        {
            issues.Add(ValidationIssue.Blocking(
                DateInFutureCode,
                $"La date d'émission du document n° {document.Number} ({RuleMessageFormat.FormatDate(document.IssueDate)}) est dans le futur. Corrigez la date dans le logiciel source avant l'envoi.",
                $"IssueDate = {RuleMessageFormat.FormatDate(document.IssueDate)} > aujourd'hui ({RuleMessageFormat.FormatDate(today)}).",
                "BT-2"));
        }
        else if (document.IssueDate < MinPlausibleDate)
        {
            issues.Add(ValidationIssue.Warning(
                DateTooOldCode,
                $"La date d'émission du document n° {document.Number} ({RuleMessageFormat.FormatDate(document.IssueDate)}) est antérieure à l'an 2000. Vérifiez la date ; le document peut être envoyé.",
                $"IssueDate = {RuleMessageFormat.FormatDate(document.IssueDate)} < {RuleMessageFormat.FormatDate(MinPlausibleDate)}.",
                "BT-2"));
        }

        if (!Iso4217Currencies.IsValid(document.CurrencyCode))
        {
            issues.Add(ValidationIssue.Blocking(
                CurrencyInvalidCode,
                $"La devise « {document.CurrencyCode} » du document n° {document.Number} n'est pas un code ISO 4217 valide. Corrigez la devise (par défaut EUR).",
                $"CurrencyCode = '{document.CurrencyCode}' absent de la liste ISO 4217.",
                "BT-5"));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
