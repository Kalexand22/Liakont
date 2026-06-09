namespace Liakont.Host.AgentManagement;

/// <summary>
/// Issue d'une action de gestion d'agent (WEB09), mappée en message opérateur français par la page.
/// </summary>
public enum AgentActionStatus
{
    /// <summary>L'action a réussi.</summary>
    Succeeded,

    /// <summary>Le nom de l'agent est obligatoire (enregistrement) — refus avant tout dispatch.</summary>
    NameRequired,

    /// <summary>L'agent est introuvable dans le tenant courant (déjà retiré, ou course).</summary>
    NotFound,

    /// <summary>
    /// Conflit d'état métier attendu (ex. rotation d'un agent révoqué, collision de préfixe de clé) :
    /// le message du domaine est porté tel quel à l'opérateur (parité avec le 409 de l'endpoint API05).
    /// </summary>
    Conflict,

    /// <summary>Échec technique inattendu (tracé) — message générique à l'opérateur.</summary>
    Failed,
}
