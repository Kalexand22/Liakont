// Liakont addition (navigation précédent/suivant en vue détail, BUG-19) - not part of the original Stratum vendoring.
namespace Stratum.Common.UI.Navigation;

using System.Collections.Generic;

/// <summary>
/// Mémoire de CIRCUIT de l'ORDRE AFFICHÉ de la dernière liste parcourue (BUG-19), pour la navigation
/// précédent/suivant en vue détail. Le gabarit <c>DeclaredListPage</c> capture, au clic d'une ligne, la liste
/// des URLs de détail dans l'ordre EXACT affiché (filtré + trié, toutes pages) ; une vue détail
/// (<c>RecordNavigator</c>) résout alors ses voisins relativement à l'URL courante. TRANSVERSE (aucune entité
/// codée en dur — l'identité est l'URL de détail elle-même) ; <c>Scoped</c> (un état par circuit/onglet Blazor,
/// JAMAIS Singleton qui mélangerait les opérateurs). Aucune persistance : la navigation suit le contexte de la
/// session en cours, pas un deep-link.
/// </summary>
public interface IListNavigationContext
{
    /// <summary>
    /// Mémorise l'ORDRE AFFICHÉ d'une liste (URLs de détail, dans l'ordre filtré + trié de la grille). Appelée par
    /// le gabarit de liste au moment où l'opérateur ouvre une fiche. Remplace tout contexte précédent.
    /// </summary>
    void Capture(IReadOnlyList<string> orderedDetailUrls);

    /// <summary>
    /// Résout les voisins (précédent / suivant) de l'URL de détail courante dans le dernier ordre capturé.
    /// Retourne <see cref="ListNavigationNeighbors.None"/> si aucune liste n'a été capturée ou si l'URL courante
    /// n'appartient pas à la liste (ex. fiche ouverte par lien direct, pas depuis la grille).
    /// </summary>
    ListNavigationNeighbors Resolve(string currentRelativeUrl);
}
