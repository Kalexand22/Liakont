namespace Liakont.Modules.Archive.Application;

/// <summary>Issue d'une tentative d'ancrage de la tête de chaîne d'un tenant (TRK06).</summary>
public enum AnchoringStatus
{
    /// <summary>La tête de chaîne a été ancrée : une preuve a été produite et archivée.</summary>
    Anchored,

    /// <summary>La tête de chaîne était déjà ancrée par cette méthode (idempotence) : aucun nouvel appel.</summary>
    AlreadyAnchored,

    /// <summary>Aucun ancrage produit par configuration (NoAnchor, ou méthode non opérationnelle en V1).</summary>
    NotAnchoredByConfiguration,

    /// <summary>Le coffre est vide : aucune tête de chaîne à ancrer.</summary>
    NothingToAnchor,
}
