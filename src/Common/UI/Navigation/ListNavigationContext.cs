// Liakont addition (navigation précédent/suivant en vue détail, BUG-19) - not part of the original Stratum vendoring.
namespace Stratum.Common.UI.Navigation;

using System;
using System.Collections.Generic;

/// <summary>
/// Implémentation en mémoire (par circuit Blazor) de <see cref="IListNavigationContext"/>. Stocke la dernière
/// liste d'URLs de détail capturée ; résout les voisins par comparaison NORMALISÉE des chemins (insensible à la
/// casse, sans slash de bordure ni query/fragment) — l'URL capturée (« /documents/{id} ») et l'URL courante
/// relative (« documents/{id} ») se rejoignent ainsi. Aucune logique métier ; aucune persistance.
/// </summary>
public sealed class ListNavigationContext : IListNavigationContext
{
    private IReadOnlyList<string> _ordered = [];

    /// <inheritdoc />
    public void Capture(IReadOnlyList<string> orderedDetailUrls) => _ordered = orderedDetailUrls ?? [];

    /// <inheritdoc />
    public ListNavigationNeighbors Resolve(string currentRelativeUrl)
    {
        if (_ordered.Count == 0 || string.IsNullOrWhiteSpace(currentRelativeUrl))
        {
            return ListNavigationNeighbors.None;
        }

        var current = Normalize(currentRelativeUrl);

        var index = -1;
        for (var i = 0; i < _ordered.Count; i++)
        {
            if (string.Equals(Normalize(_ordered[i]), current, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return ListNavigationNeighbors.None;
        }

        var previous = index > 0 ? _ordered[index - 1] : null;
        var next = index < _ordered.Count - 1 ? _ordered[index + 1] : null;
        return new ListNavigationNeighbors(previous, next, index, _ordered.Count);
    }

    /// <summary>Chemin comparable : sans query/fragment, sans slash de bordure (les URLs absolues/relatives convergent).</summary>
    private static string Normalize(string url)
    {
        var path = url;

        var queryAt = path.IndexOfAny(['?', '#']);
        if (queryAt >= 0)
        {
            path = path[..queryAt];
        }

        return path.Trim().Trim('/');
    }
}
