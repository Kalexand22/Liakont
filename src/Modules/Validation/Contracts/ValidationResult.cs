namespace Liakont.Modules.Validation.Contracts;

/// <summary>
/// Résultat agrégé de la validation d'un document : l'ensemble des anomalies détectées par le
/// pipeline (F04 §5). Immuable.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>Crée un résultat de validation à partir des anomalies agrégées.</summary>
    /// <param name="issues">Anomalies détectées (bloquantes et alertes). Jamais <c>null</c>.</param>
    public ValidationResult(IReadOnlyList<ValidationIssue> issues)
    {
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    /// <summary>Anomalies détectées (bloquantes et alertes confondues). Jamais <c>null</c>.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>
    /// Vrai si AUCUNE anomalie bloquante n'a été détectée : le document peut être envoyé. Les
    /// alertes (Warning) n'invalident pas le document (F04 §3.3). F04 §5 nomme l'inverse « IsBlocking ».
    /// </summary>
    public bool IsValid => !HasBlockingIssue;

    /// <summary>Vrai si au moins une anomalie bloquante a été détectée (équivaut au « IsBlocking » de F04 §5).</summary>
    public bool HasBlockingIssue => Issues.Any(issue => issue.Severity == ValidationSeverity.Blocking);
}
