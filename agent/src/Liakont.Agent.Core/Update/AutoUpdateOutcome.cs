namespace Liakont.Agent.Core.Update;

/// <summary>
/// Issue MÉCANIQUE d'une tentative d'auto-update (AGT04, F12 §2.5). L'agent n'interprète aucun état
/// métier : il décrit ce qu'il a fait du paquet proposé par la plateforme (refusé / différé / lancé).
/// </summary>
public enum AutoUpdateOutcome
{
    /// <summary>Aucune mise à jour demandée (ni <c>updateRequired</c>, ni 426).</summary>
    NotRequested = 0,

    /// <summary>Demandée mais sans URL de manifeste exploitable — rien à faire.</summary>
    NoManifestUrl = 1,

    /// <summary>La version proposée n'est pas strictement supérieure à la version courante (anti-downgrade).</summary>
    AlreadyCurrent = 2,

    /// <summary>Différée : un run d'extraction est en cours (jamais d'update pendant un run).</summary>
    DeferredRunInProgress = 3,

    /// <summary>Refusée : aucune clé publique de signature provisionnée (fail-closed).</summary>
    MissingSigningKey = 4,

    /// <summary>Refusée : manifeste illisible ou incomplet.</summary>
    InvalidManifest = 5,

    /// <summary>Refusée : signature du manifeste invalide (provenance non prouvée).</summary>
    RejectedSignature = 6,

    /// <summary>Refusée : empreinte SHA-256 du paquet non concordante avec le manifeste signé.</summary>
    RejectedHash = 7,

    /// <summary>Échec de téléchargement (manifeste ou paquet injoignable).</summary>
    DownloadFailed = 8,

    /// <summary>Updater détaché lancé : le remplacement des binaires se poursuit hors de ce processus.</summary>
    Launched = 9,

    /// <summary>Échec inattendu pendant la préparation de la mise à jour.</summary>
    Failed = 10,

    /// <summary>Une mise à jour est déjà en cours dans ce processus (garde de ré-entrance).</summary>
    AlreadyInProgress = 11,
}
