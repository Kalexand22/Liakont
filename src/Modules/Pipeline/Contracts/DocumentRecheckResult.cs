namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>Issue d'une re-vérification de document (item API02b, <see cref="IDocumentRecheckService"/>).</summary>
public enum DocumentRecheckOutcome
{
    /// <summary>Aucun document de cet identifiant dans le tenant courant (→ 404).</summary>
    NotFound,

    /// <summary>Le document n'est pas dans l'état <c>Blocked</c> : la re-vérification ne s'applique pas (→ 409).</summary>
    NotBlocked,

    /// <summary>Le contenu pivot stagé est indisponible (pas encore stagé, ou altéré/illisible) : impossible de re-vérifier (→ 409).</summary>
    ContentUnavailable,

    /// <summary>Le document passe désormais : transition <c>Blocked → ReadyToSend</c> effectuée.</summary>
    ReadyToSend,

    /// <summary>Le document reste bloqué avec les nouveaux motifs (aucune transition d'état — <c>Blocked → Blocked</c> interdit — mais un fait d'audit append-only de re-vérification est inscrit, item FIX02).</summary>
    StillBlocked,
}

/// <summary>
/// Résultat d'une re-vérification (item API02b). Porte l'issue, l'état résultant et — si le document reste
/// bloqué — les motifs frais (message opérateur agrégé), pour affichage immédiat dans la console (WEB03b) sans
/// rechargement. Le « toujours bloqué » n'emporte AUCUNE transition d'état (la machine à états interdit
/// <c>Blocked → Blocked</c>), mais le service de re-vérification l'INSCRIT dans la piste d'audit append-only via
/// un événement <c>RecheckedStillBlocked</c> (item FIX02) — geste opérateur + motif réévalué tracés et persistés ;
/// ce résultat n'est donc pas la SEULE trace, il sert l'affichage immédiat.
/// </summary>
public sealed record DocumentRecheckResult
{
    /// <summary>Issue de la re-vérification.</summary>
    public required DocumentRecheckOutcome Outcome { get; init; }

    /// <summary>État du document après re-vérification (<c>ReadyToSend</c> ou <c>Blocked</c>), ou état courant pour <see cref="DocumentRecheckOutcome.NotBlocked"/> ; <c>null</c> sinon.</summary>
    public string? State { get; init; }

    /// <summary>Motif(s) de blocage frais (message opérateur agrégé) si <see cref="Outcome"/> = <see cref="DocumentRecheckOutcome.StillBlocked"/> ; <c>null</c> sinon.</summary>
    public string? BlockingReason { get; init; }

    /// <summary>Document introuvable dans le tenant.</summary>
    public static DocumentRecheckResult NotFound() => new() { Outcome = DocumentRecheckOutcome.NotFound };

    /// <summary>Document présent mais pas <c>Blocked</c> (état courant porté pour le message opérateur).</summary>
    public static DocumentRecheckResult NotBlocked(string state) => new() { Outcome = DocumentRecheckOutcome.NotBlocked, State = state };

    /// <summary>Contenu pivot stagé indisponible (pas encore stagé ou altéré).</summary>
    public static DocumentRecheckResult ContentUnavailable() => new() { Outcome = DocumentRecheckOutcome.ContentUnavailable, State = "Blocked" };

    /// <summary>Document débloqué : <c>Blocked → ReadyToSend</c>.</summary>
    public static DocumentRecheckResult ReadyToSend() => new() { Outcome = DocumentRecheckOutcome.ReadyToSend, State = "ReadyToSend" };

    /// <summary>Document toujours bloqué avec les nouveaux motifs (aucune transition).</summary>
    public static DocumentRecheckResult StillBlocked(string blockingReason) => new() { Outcome = DocumentRecheckOutcome.StillBlocked, State = "Blocked", BlockingReason = blockingReason };
}
