namespace Liakont.Modules.Validation.Contracts;

/// <summary>
/// Une anomalie détectée par une règle de validation (F04 §5). Immuable. Le message opérateur est
/// en français, actionnable, et cite le numéro de document concerné (CLAUDE.md n°12).
/// </summary>
public sealed class ValidationIssue
{
    /// <summary>Crée une anomalie de validation.</summary>
    /// <param name="code">Code stable de l'anomalie (ex. <c>DOC_TOTAL_MISMATCH</c>). Obligatoire.</param>
    /// <param name="severity">Sévérité (bloquante ou alerte).</param>
    /// <param name="messageOperateur">Message opérateur en français, actionnable. Obligatoire.</param>
    /// <param name="detailTechnique">Détail technique pour le journal/audit (jamais affiché à l'opérateur).</param>
    /// <param name="fieldRef">Champ EN 16931 (BT-xxx) ou champ pivot concerné (F04 §5).</param>
    public ValidationIssue(
        string code,
        ValidationSeverity severity,
        string messageOperateur,
        string? detailTechnique = null,
        string? fieldRef = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Le code de l'anomalie est obligatoire.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(messageOperateur))
        {
            throw new ArgumentException("Le message opérateur est obligatoire.", nameof(messageOperateur));
        }

        Code = code;
        Severity = severity;
        MessageOperateur = messageOperateur;
        DetailTechnique = detailTechnique;
        FieldRef = fieldRef;
    }

    /// <summary>Code stable de l'anomalie (ex. <c>DOC_TOTAL_MISMATCH</c>).</summary>
    public string Code { get; }

    /// <summary>Sévérité de l'anomalie (bloquante ou alerte).</summary>
    public ValidationSeverity Severity { get; }

    /// <summary>Message opérateur en français, actionnable, citant le numéro de document (F04 §5).</summary>
    public string MessageOperateur { get; }

    /// <summary>Détail technique pour le journal/audit (non affiché à l'opérateur). Optionnel.</summary>
    public string? DetailTechnique { get; }

    /// <summary>Champ EN 16931 (BT-xxx) ou champ pivot concerné (F04 §5). Optionnel.</summary>
    public string? FieldRef { get; }

    /// <summary>Fabrique une anomalie BLOQUANTE (le document ne sera pas envoyé).</summary>
    public static ValidationIssue Blocking(
        string code,
        string messageOperateur,
        string? detailTechnique = null,
        string? fieldRef = null)
    {
        return new ValidationIssue(code, ValidationSeverity.Blocking, messageOperateur, detailTechnique, fieldRef);
    }

    /// <summary>Fabrique une ALERTE (l'envoi reste possible, l'opérateur est averti).</summary>
    public static ValidationIssue Warning(
        string code,
        string messageOperateur,
        string? detailTechnique = null,
        string? fieldRef = null)
    {
        return new ValidationIssue(code, ValidationSeverity.Warning, messageOperateur, detailTechnique, fieldRef);
    }
}
