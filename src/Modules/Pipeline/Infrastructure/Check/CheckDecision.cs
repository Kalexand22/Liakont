namespace Liakont.Modules.Pipeline.Infrastructure.Check;

/// <summary>
/// Décision FINALE du CHECK pour un document : soit un motif de blocage (message opérateur), soit la version
/// de table de mapping appliquée (document prêt à l'envoi). Distincte de <see cref="CheckEvaluation"/> — qui
/// ne couvre QUE le mapping TVA : <see cref="CheckDecision"/> intègre AUSSI la garde-fou production et la
/// validation, c'est-à-dire la décision complète de <see cref="DocumentCheckEvaluator"/>.
/// </summary>
internal sealed record CheckDecision
{
    private CheckDecision()
    {
    }

    /// <summary>Motif de blocage agrégé (opérateur) ; <c>null</c> si le document est prêt à l'envoi.</summary>
    public string? BlockReason { get; private init; }

    /// <summary>Version de la table de mapping appliquée ; <c>null</c> si bloqué.</summary>
    public string? MappingVersion { get; private init; }

    /// <summary>Vrai si le document est prêt à l'envoi (aucun motif de blocage).</summary>
    public bool IsReady => BlockReason is null;

    /// <summary>Crée une décision « bloqué » avec son motif opérateur.</summary>
    public static CheckDecision Blocked(string reason) => new() { BlockReason = reason };

    /// <summary>Crée une décision « prêt » avec la version de table appliquée.</summary>
    public static CheckDecision Ready(string? mappingVersion) => new() { MappingVersion = mappingVersion };
}
