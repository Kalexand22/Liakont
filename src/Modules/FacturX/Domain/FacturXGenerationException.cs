namespace Liakont.Modules.FacturX.Domain;

/// <summary>
/// Levée par le sérialiseur CII (FX03) / le scellement PDF/A-3 (FX04) quand un élément obligatoire
/// EN 16931 n'est <b>ni porté ni dérivable</b> par agrégation normative, ou quand les agrégats dérivés
/// (BG-23, BT-106, BT-115) ne se <b>réconcilient pas</b> avec les totaux portés par le pivot
/// (BR-CO-14/15/16). On BLOQUE plutôt que d'émettre un Factur-X non conforme ou une valeur fabriquée
/// (ADR-0023 INV-FX-2/7 ; CLAUDE.md n°2/3). Le message est opérateur, en français, avec le numéro de
/// document et l'action corrective (CLAUDE.md n°12).
/// </summary>
public sealed class FacturXGenerationException : Exception
{
    /// <summary>Crée l'exception de blocage pour un document donné.</summary>
    /// <param name="documentNumber">Numéro du document pivot (BT-1).</param>
    /// <param name="message">Message opérateur en français (cause + action corrective).</param>
    public FacturXGenerationException(string documentNumber, string message)
        : base(message)
    {
        DocumentNumber = documentNumber ?? string.Empty;
    }

    /// <summary>Numéro du document pivot (BT-1) concerné, pour le message opérateur et la trace.</summary>
    public string DocumentNumber { get; }
}
