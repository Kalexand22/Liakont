namespace Liakont.Modules.Pipeline.Domain.Margin;

using System;

/// <summary>
/// Levée par <see cref="MarginCalculator"/> quand un document de marge ferait apparaître une TVA
/// DISTINCTE (un total de TVA non nul, ou une ligne portant une ventilation de TVA &gt; 0). Sous le
/// régime de la marge, le montant de marge est une BASE et aucune TVA n'y figure distinctement
/// (CGI art. 297 E ; F03 §2.3/§2.4, F07-F08:36). C'est un CRITÈRE BLOQUANT (CLAUDE.md n°3 « bloquer
/// plutôt qu'envoyer faux ») : on ne calcule jamais une marge sur un document qui viole l'art. 297 E.
/// </summary>
public sealed class MarginVatNotSeparableException : Exception
{
    public MarginVatNotSeparableException()
    {
    }

    public MarginVatNotSeparableException(string message)
        : base(message)
    {
    }

    public MarginVatNotSeparableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'exception (message opérateur FR, CLAUDE.md n°12) pour un document en violation.</summary>
    public static MarginVatNotSeparableException ForDocument(string documentNumber) =>
        new($"Document « {documentNumber} » : impossible de calculer la marge — une TVA distincte y figure, " +
            "ce qui est interdit sous le régime de la marge (CGI art. 297 E). Le montant de marge est une base, " +
            "sans TVA distincte. Action opérateur : vérifiez la donnée source (le bordereau ne doit porter aucune " +
            "TVA distincte sur le montant de marge).");
}
