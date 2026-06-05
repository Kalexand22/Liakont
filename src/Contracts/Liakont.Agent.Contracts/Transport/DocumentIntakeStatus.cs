namespace Liakont.Agent.Contracts.Transport;

/// <summary>
/// État de prise en charge d'un document poussé, rapporté par le point de statut
/// (GET /api/agent/v1/documents/status — ADR-0012). C'est la PLATEFORME qui détermine cet état ;
/// l'agent le LIT et applique une règle mécanique (purger / renvoyer / signaler) sans jamais
/// interpréter l'état fiscal (CLAUDE.md n°6).
/// </summary>
public enum DocumentIntakeStatus
{
    /// <summary>Reçu mais pas encore rangé durablement dans le pipeline — l'agent RENVOIE l'élément (non terminal).</summary>
    Pending = 1,

    /// <summary>Durablement créé et entré dans le pipeline (le Detected existe) — terminal OK : l'agent purge l'élément.</summary>
    Processed = 2,

    /// <summary>Rejeté définitivement (payload non conforme au contrat) — terminal : l'agent purge et signale à l'opérateur, sans re-pousser.</summary>
    Rejected = 3,
}
