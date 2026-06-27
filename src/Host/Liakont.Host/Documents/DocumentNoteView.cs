namespace Liakont.Host.Documents;

/// <summary>
/// Une mention de niveau document, prête à afficher dans l'onglet « Contenu » du détail (BUG-26, F12-A §3.4 :
/// mentions de facturation effectives). PROJECTION en lecture du pivot (<see cref="DocumentLineProjection"/>) —
/// aucune règle métier, aucun texte inventé. Porte le code sujet BRUT (BT-21, ex. <c>PMD</c> / <c>PMT</c> /
/// <c>AAB</c>) ; le LIBELLÉ français du code est dérivé par la vue (présentation pure), jamais ici.
/// </summary>
public sealed record DocumentNoteView
{
    /// <summary>Texte de la mention (EN 16931 BT-22).</summary>
    public required string Content { get; init; }

    /// <summary>Code sujet UNTDID 4451 (EN 16931 BT-21), ou <c>null</c> pour une note libre sans code.</summary>
    public required string? SubjectCode { get; init; }
}
