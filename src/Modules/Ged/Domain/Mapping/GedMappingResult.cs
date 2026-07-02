namespace Liakont.Modules.Ged.Domain.Mapping;

using System;

/// <summary>
/// Résultat d'un mapping GED (F19 §4.5, INV-GED-05) : soit <b>mappé</b> (<see cref="Document"/> renseigné), soit
/// <b>déféré</b> (<see cref="DeferReason"/> renseigné). Vocabulaire figé <b>DEFER, jamais BLOCK</b> : l'enjeu GED
/// n'est pas fiscal — un <c>documentType</c> sans profil validé, un axe obligatoire non résolu ou une valeur
/// source ambiguë RANGE le document en <c>deferred</c> (visible console), jamais mappé au hasard ni rejeté en
/// silence (règle 3). Miroir de <c>MappingResult</c> (domaine TVA), où le déféré remplace le blocage.
/// </summary>
public sealed class GedMappingResult
{
    private GedMappingResult(MappedDocument? document, string? deferReason)
    {
        Document = document;
        DeferReason = deferReason;
    }

    /// <summary>Vrai si le document a été mappé ; faux s'il est déféré.</summary>
    public bool IsMapped => Document is not null;

    /// <summary>Vrai si le document est déféré (rangé en attente d'un profil / d'une résolution).</summary>
    public bool IsDeferred => Document is null;

    /// <summary>Le pivot GED mappé (renseigné ssi <see cref="IsMapped"/>).</summary>
    public MappedDocument? Document { get; }

    /// <summary>Le motif de déférement, en français avec action corrective (renseigné ssi <see cref="IsDeferred"/>).</summary>
    public string? DeferReason { get; }

    /// <summary>Crée un résultat MAPPÉ.</summary>
    /// <param name="document">Le pivot GED mappé.</param>
    /// <returns>Un résultat mappé.</returns>
    public static GedMappingResult Mapped(MappedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new GedMappingResult(document, deferReason: null);
    }

    /// <summary>Crée un résultat DÉFÉRÉ.</summary>
    /// <param name="reason">Le motif de déférement (français, action corrective).</param>
    /// <returns>Un résultat déféré.</returns>
    public static GedMappingResult Deferred(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Un déférement doit porter un motif.", nameof(reason));
        }

        return new GedMappingResult(document: null, reason);
    }
}
