// Liakont addition (navigation précédent/suivant en vue détail, BUG-19) - not part of the original Stratum vendoring.
namespace Stratum.Common.UI.Navigation;

/// <summary>
/// Voisins d'un enregistrement dans l'ordre affiché d'une liste (BUG-19) : URL de détail du précédent / suivant
/// (<c>null</c> aux bornes), position 0-based et total. <see cref="HasContext"/> est faux quand l'enregistrement
/// courant n'appartient à aucune liste capturée (la vue détail n'affiche alors pas de navigation).
/// </summary>
public readonly record struct ListNavigationNeighbors(string? Previous, string? Next, int Index, int Total)
{
    /// <summary>Aucun contexte de liste (fiche ouverte hors d'une grille, ou liste non capturée).</summary>
    public static readonly ListNavigationNeighbors None = new(null, null, -1, 0);

    /// <summary>Vrai quand l'enregistrement courant a été localisé dans une liste capturée (navigation affichable).</summary>
    public bool HasContext => Total > 0 && Index >= 0;
}
