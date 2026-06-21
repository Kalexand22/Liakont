namespace Liakont.Agent.Core.Net;

using System.Net;

/// <summary>
/// Bootstrap TLS process-wide de l'agent net48 (RDF01, ADR-0001 règle 5, F12 §2.6).
/// <see cref="ServicePointManager"/> est un statique GLOBAL au processus, NON partagé entre les
/// exécutables de l'agent (service, CLI, updater). Chaque exécutable doit donc forcer TLS 1.2/1.3
/// AU DÉMARRAGE : sur un hôte dont la clé de registre <c>SchUseStrongCrypto</c> n'est pas posée,
/// net48 peut négocier TLS 1.0/1.1 et le canal sortant RÉEL devient faible — alors que la sonde
/// <c>test-api</c>, qui tourne dans un AUTRE processus, valide en vert (faux-vert, CLAUDE.md règle
/// review n°8). Aucune logique métier (ADR-0001 règle 5) : on pose le protocole, rien d'autre.
/// </summary>
public static class AgentTls
{
    /// <summary>
    /// Ajoute TLS 1.2 et TLS 1.3 aux protocoles autorisés du processus. OU bit-à-bit : n'efface
    /// jamais un protocole déjà actif, idempotent. À appeler au tout début du <c>Main</c> de chaque
    /// exécutable agent ET sur le chemin de run réel (<c>AgentRunComposition</c>), avant toute
    /// connexion HTTPS sortante.
    /// </summary>
    public static void ForceStrongTls()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
    }
}
