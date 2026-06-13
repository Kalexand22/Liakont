namespace Liakont.Host.Documents;

/// <summary>
/// Message opérateur UNIQUE de la suspension des envois (table TVA non validée — TVA01 §5), partagé
/// par les DEUX points d'entrée d'envoi de la console (liste « Tout envoyer », fiche « Envoyer »)
/// pour une affordance cohérente. La suspension affichée est purement UX : la garde réelle reste
/// côté serveur et n'est jamais affaiblie (CLAUDE.md n°3).
/// </summary>
internal static class DocumentSendSuspension
{
    /// <summary>Motif affiché (hint + infobulle) quand l'envoi est désactivé.</summary>
    public const string Reason =
        "Les envois sont suspendus : la table TVA n'est pas validée par l'expert-comptable.";
}
