namespace Liakont.Host.Documents;

using System;

/// <summary>
/// Mémoire de circuit des derniers filtres appliqués sur la page Documents (période, état, type) —
/// issue GitHub #33. Le « Retour à la liste » de la fiche détail est un lien statique <c>/documents</c>
/// sans query string : sans cette mémoire, les filtres de l'opérateur seraient perdus à chaque retour.
/// Portée SCOPED = le circuit Blazor (un utilisateur, un onglet navigateur) : aucun état partagé entre
/// utilisateurs ni entre tenants. L'URL reste la source de vérité prioritaire (lien partageable) ;
/// cette mémoire n'est lue qu'en l'absence de paramètres de filtre dans l'URL.
/// </summary>
internal sealed class DocumentsListFilterMemory
{
    /// <summary>Début de la dernière période consultée, ou <c>null</c> si la page n'a pas encore été visitée.</summary>
    public DateOnly? From { get; private set; }

    /// <summary>Fin de la dernière période consultée.</summary>
    public DateOnly? To { get; private set; }

    /// <summary>Dernier filtre État (clé d'état brute), ou <c>null</c> pour « Tous ».</summary>
    public string? State { get; private set; }

    /// <summary>Dernier filtre Type (libellé d'affichage), ou <c>null</c> pour « Tous ».</summary>
    public string? TypeLabel { get; private set; }

    /// <summary>Mémorise l'état courant des filtres de la liste.</summary>
    public void Remember(DateOnly from, DateOnly to, string? state, string? typeLabel)
    {
        From = from;
        To = to;
        State = state;
        TypeLabel = typeLabel;
    }
}
