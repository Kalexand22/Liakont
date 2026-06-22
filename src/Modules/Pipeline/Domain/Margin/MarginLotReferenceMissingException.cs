namespace Liakont.Modules.Pipeline.Domain.Margin;

using System;

/// <summary>
/// Levée par <see cref="MarginCalculator"/> quand un frais (acheteur ou vendeur) ne porte pas de
/// référence de lot (<c>LotReference</c> null ou vide). La référence de lot est la clé d'agrégation
/// de la marge par opération (no_ba) ; sans elle, le calcul est impossible. C'est un critère BLOQUANT
/// (CLAUDE.md n°3 « bloquer plutôt qu'envoyer faux »).
/// </summary>
public sealed class MarginLotReferenceMissingException : Exception
{
    public MarginLotReferenceMissingException()
    {
    }

    public MarginLotReferenceMissingException(string message)
        : base(message)
    {
    }

    public MarginLotReferenceMissingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'exception (message opérateur FR, CLAUDE.md n°12) pour un document en erreur.</summary>
    public static MarginLotReferenceMissingException ForDocument(string documentNumber) =>
        new($"Document « {documentNumber} » : impossible de calculer la marge — un frais (acheteur ou vendeur) " +
            "ne porte pas de référence de lot (no_ba). La référence de lot est obligatoire pour agréger la marge " +
            "par opération. Action opérateur : vérifiez la donnée source et assurez-vous que chaque frais porte " +
            "un no_ba valide.");
}
