namespace Liakont.Agent.Core.Transport;

/// <summary>
/// Résultat d'un push de PDF (lié ou pool). Un PDF n'est pas un document fiscal : son acquittement est
/// en UN temps (succès = retrait de la file), contrairement aux documents pivot (ACK en deux temps).
/// </summary>
public sealed class PdfPushOutcome
{
    /// <summary>Crée un résultat de push de PDF.</summary>
    /// <param name="kind">Catégorie de réponse de la plateforme.</param>
    /// <param name="reason">Détail (diagnostic / motif d'échec), si applicable.</param>
    public PdfPushOutcome(PlatformResponseKind kind, string? reason = null)
    {
        Kind = kind;
        Reason = reason;
    }

    /// <summary>Catégorie de réponse de la plateforme.</summary>
    public PlatformResponseKind Kind { get; }

    /// <summary>Détail (diagnostic / motif d'échec).</summary>
    public string? Reason { get; }
}
